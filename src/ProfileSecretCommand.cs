using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Profile secret sub-domain (architecture-recovery issue 26, ticket 26 split).
/// Extracted from ProfileCommand.cs: ProfileAddSecret / ProfileEditSecret / ProfileRemoveSecret /
/// ProfileRevealSecret / RunSecretProviderCommand / ProfileExportSecrets / ProfileImportSecrets
/// plus TryDecryptSafe. Behaviour is verbatim from the original 1605-line monolith; the
/// splitting rationale is per Q5 ticket 26 ("launch/secret-provider first, then CRUD") and
/// Q2 ticket 26 ("type-level split is ticket 27, file-level split here").
///
/// Behaviour is frozen by ProfileCommandCharacterizationTests (ticket 32 .verified.txt
/// snapshots). Any drift in the secret verbs must turn those snapshots red -- do not
/// reword error messages or reorder audit lines without updating the snapshots in the
/// same PR.
/// </summary>
partial class Program
{
    // Dispatch entry point installed by RunProfileCommand in ProfileCommand.cs.
    // Routes the seven secret verbs into their dedicated handlers.
    internal static int RunProfileSecretCommand(string[] args)
    {
        string sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "add-secret" => args.Length < 5 ? ArgError("Usage: env-manager profile add-secret <profile> <name> <value>") : ProfileAddSecret(args[2], args[3], args[4]),
            "edit-secret" => args.Length < 6 ? ArgError("Usage: env-manager profile edit-secret <profile> <old> <new> <value>") : ProfileEditSecret(args[2], args[3], args[4], args[5]),
            "remove-secret" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-secret <profile> <name>") : ProfileRemoveSecret(args[2], args[3]),
            "reveal-secret" => args.Length < 4 ? ArgError("Usage: env-manager profile reveal-secret <profile> <name>") : ProfileRevealSecret(args[2], args[3]),
            "export-secrets" => ProfileExportSecrets(args),
            "import-secrets" => ProfileImportSecrets(args),
            "secret-provider" => RunSecretProviderCommand(args),
            _ => ArgError($"Unknown profile secret subcommand: {sub}")
        };
    }

    // v0.7 DPAPI-encrypted secret variables for launch profiles.
    // Plaintext lives only in transient CLI process memory; the profiles.json stores
    // base64(DPAPI-Protect(CurrentUser, plaintext)). AGENTS.md hard boundary: audit
    // records the variable NAME only, never its plaintext or ciphertext value.
    static int ProfileAddSecret(string profileName, string varName, string varValue)
    {
        if (string.IsNullOrWhiteSpace(varName) || varName.Length > 255 || varName.Contains('=') || ProtectedSystemVars.Contains(varName))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return 1;
        }
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        // v0.7.5: secrets are meaningful only on Launch (local) profiles. A
        // Global profile cannot be applied (IsProfileApplicable rejects any
        // profile containing SecretVariables) and a decrypted plaintext can
        // never reach a process env block via the Global write path (Global
        // writes to the registry). Prohibit at entry so users do not encrypt
        // a secret on a Global profile and discover later that it is inert.
        if (!profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
            return ArgError("Error: Secrets can only be added to Launch (local) profiles. Use \"profile set-launch <name> --target <exe>\" to convert a Global profile to Launch first.");
        // v0.7.5: same invariant as AddSecret - reject non-launch profiles.
        if (!profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
            return ArgError("Error: Secrets can only be edited on Launch (local) profiles. Use \"profile set-launch <name> --target <exe>\" to convert first.");
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing its variables");

        profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        string encrypted = SecretProviderManager.Encrypt(varValue, profileName + "\\" + varName);
        profile.Variables.Add(new ProfileVariable { Name = varName, Value = encrypted });
        if (!profile.SecretVariables.Any(s => s.Equals(varName, StringComparison.OrdinalIgnoreCase)))
            profile.SecretVariables.Add(varName);
        SaveProfiles(profiles);

        RecordProfileAudit("profile add-secret", profileName,
            JsonSerializer.Serialize(new { name = varName, value = "<redacted>" }),
            JsonSerializer.Serialize(new { name = varName, value = "<encrypted>" }));
        Console.WriteLine($"Added secret variable '{varName}' ({SecretProviderManager.GetActiveProviderName()}) to profile '{profileName}'");
        return 0;
    }

    static int ProfileEditSecret(string profileName, string oldVarName, string newVarName, string newVarValue)
    {
        // v0.7: reject rename into a protected system variable name (same invariant as add-secret / AGENTS.md hard boundary).
        if (string.IsNullOrWhiteSpace(newVarName) || newVarName.Length > 255 || newVarName.Contains('=') || newVarName.Contains('\x00') || newVarName.Contains('\n') || newVarName.Contains('\r') || ProtectedSystemVars.Contains(newVarName))
        {
            Console.Error.WriteLine("Error: Invalid or protected variable name");
            return 1;
        }
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing its variables");

        var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
        if (v == null) { Console.Error.WriteLine($"Error: Secret variable '{oldVarName}' not found in profile '{profileName}'"); return 1; }

        bool wasMarkedSecret = profile.SecretVariables.Any(s => s.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
        string encryptedNew = SecretProviderManager.Encrypt(newVarValue, profileName + "\\" + newVarName);
        v.Name = newVarName;
        v.Value = encryptedNew;

        if (!newVarName.Equals(oldVarName, StringComparison.OrdinalIgnoreCase))
        {
            profile.SecretVariables.RemoveAll(s => s.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
            if (wasMarkedSecret && !profile.SecretVariables.Any(s => s.Equals(newVarName, StringComparison.OrdinalIgnoreCase)))
                profile.SecretVariables.Add(newVarName);
        }
        SaveProfiles(profiles);

        RecordProfileAudit("profile edit-secret", profileName,
            JsonSerializer.Serialize(new { name = oldVarName, value = "<redacted>" }),
            JsonSerializer.Serialize(new { name = newVarName, value = "<encrypted>" }));
        Console.WriteLine($"Edited secret '{oldVarName}' -> '{newVarName}' in profile '{profileName}'");
        return 0;
    }

    static int ProfileRemoveSecret(string profileName, string varName)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing its variables");

        var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (v == null) { Console.Error.WriteLine($"Error: Variable '{varName}' not found in profile '{profileName}'"); return 1; }

        // Delete provider-side state (e.g. CredMan entry) before removing from profile
        try { SecretProviderManager.Delete(v.Value, profileName + "\\" + varName); } catch { }

        profile.Variables.Remove(v);
        profile.SecretVariables.RemoveAll(s => s.Equals(varName, StringComparison.OrdinalIgnoreCase));
        SaveProfiles(profiles);

        RecordProfileAudit("profile remove-secret", profileName, JsonSerializer.Serialize(new { name = varName }), null);
        Console.WriteLine($"Removed secret variable '{varName}' from profile '{profileName}'");
        return 0;
    }

    // Reveal one secret's plaintext to stdout. Only succeeds for the same user account
    // that encrypted it (DPAPI CurrentUser). `profile launch` decrypts in-process; this
    // command exists only for the rare case where the agent actually needs the raw value
    // (e.g. piping into a credential helper). Use sparingly to limit plaintext exposure.
    static int ProfileRevealSecret(string profileName, string varName)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (v == null) { Console.Error.WriteLine($"Error: Variable '{varName}' not found in profile '{profileName}'"); return 1; }
        if (!profile.SecretVariables.Any(s => s.Equals(varName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Error: Variable '{varName}' is not a secret in profile '{profileName}'");
            return 1;
        }
        try
        {
            // v0.8.0: resolve mount reference if the value is a "mount:" prefixed ID.
            var envelope = ResolveSecretMount(v.Value) ?? v.Value;
            using var secret = new SecretString(SecretProviderManager.Decrypt(envelope, profileName + "\\" + varName));
            // Audit BEFORE printing plaintext so the audit trail records the
            // fact that a secret was revealed (for security forensics) but
            // never the value itself. Marked <redacted> twice over.
            RecordProfileAudit("profile reveal-secret", profileName,
                JsonSerializer.Serialize(new { name = varName, value = "<redacted>" }),
                JsonSerializer.Serialize(new { name = varName, value = "<revealed>" }));
            Console.Out.Write(secret.AsSpan());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to decrypt secret '{varName}': {ScrubExceptionMessage(ex.Message)}");
            return 1;
        }
    }

    // v0.8 Secret provider management
    static int RunSecretProviderCommand(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: env-manager profile secret-provider <list|set> [options]");
            Console.Error.WriteLine("  secret-provider list              List available providers and active selection");
            Console.Error.WriteLine("  secret-provider set <name>        Set the active secret provider");
            return 1;
        }
        string sub = args[2];
        switch (sub)
        {
            case "list":
                var providers = SecretProviderManager.ListProviders();
                string active = SecretProviderManager.GetActiveProviderName();
                Console.WriteLine($"Active provider: {active}");
                Console.WriteLine("Available providers:");
                foreach (var (name, available) in providers)
                {
                    string marker = name.Equals(active, StringComparison.OrdinalIgnoreCase) ? " (active)" : "";
                    Console.WriteLine($"  {name}{marker}{(available ? "" : " (unavailable)")}");
                }
                return 0;

            case "set":
                if (args.Length < 4)
                {
                    Console.Error.WriteLine("Usage: env-manager profile secret-provider set <name>");
                    return 1;
                }
                try
                {
                    SecretProviderManager.SetActiveProvider(args[3]);
                    // v0.7.12: audit the active-provider switch. Even though
                    // this is a config-only change (no secret data is mutated),
                    // recording it lets users see this atomic security-significant
                    // transaction in the history view (per item 7 mandate) and
                    // detect silent provider swaps.
                    RecordProfileAudit("profile secret-provider set", args[3],
                        null, JsonSerializer.Serialize(new { provider = args[3] }));
                    Console.WriteLine($"Active secret provider set to: {args[3]}");
                    Console.WriteLine("Note: existing secrets encrypted with the previous provider will still decrypt correctly (fail-closed on unknown provider).");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ScrubExceptionMessage(ex.Message)}");
                    return 1;
                }

            case "rotate":
                var profilesToRotate = LoadProfiles();
                var (total, rotatedN, failedN) = SecretProviderManager.RotateAll(profilesToRotate);
                if (rotatedN > 0) SaveProfiles(profilesToRotate);
                Console.WriteLine($"Rotation complete: {rotatedN}/{total} secrets re-encrypted, {failedN} failed");
                if (rotatedN > 0)
                {
                    var rotatedProfileNames = profilesToRotate
                        .Where(p => p.SecretVariables.Count > 0)
                        .Select(p => p.Name)
                        .ToList();
                    var auditName = rotatedProfileNames.Count > 0
                        ? string.Join(", ", rotatedProfileNames)
                        : "(none)";
                    RecordProfileAudit("profile secret-provider rotate", auditName,
                        JsonSerializer.Serialize(new { total, rotated = rotatedN, failed = failedN, profiles = rotatedProfileNames }), null);
                }
                return 0;

            default:
                Console.Error.WriteLine($"Unknown secret-provider subcommand: {sub}");
                return 1;
        }
    }

    // v0.8 Phase 3: Export secrets from a profile to an encrypted file
    static int ProfileExportSecrets(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: env-manager profile export-secrets <profile> <output-file>");
            return 1;
        }
        string profileName = args[2];
        string outputFile = args[3];
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }

        // Validate output path
        string? pathError = ValidateFilePath(outputFile, mustExist: false);
        if (pathError != null) { Console.Error.WriteLine($"Error: {pathError}"); return 1; }

        string encrypted = SecretProviderManager.ExportSecrets(profile);
        WriteAtomicUtf8(outputFile, encrypted);
        int secretCount = profile.SecretVariables.Count;
        Console.WriteLine($"Exported {secretCount} secret(s) from profile '{profileName}' to '{outputFile}'");
        RecordProfileAudit("profile export-secrets", profileName,
            JsonSerializer.Serialize(new { file = outputFile, count = secretCount }), null);
        return 0;
    }

    // v0.8 Phase 3: Import secrets from an encrypted file into a profile
    static int ProfileImportSecrets(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: env-manager profile import-secrets <profile> <input-file>");
            return 1;
        }
        string profileName = args[2];
        string inputFile = args[3];
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before importing secrets");

        // Validate input path
        string? pathError = ValidateFilePath(inputFile, mustExist: true);
        if (pathError != null) { Console.Error.WriteLine($"Error: {pathError}"); return 1; }
        if (!File.Exists(inputFile)) { Console.Error.WriteLine($"Error: File '{inputFile}' not found"); return 1; }

        string encryptedBackup = File.ReadAllText(inputFile);
        var results = SecretProviderManager.ImportSecrets(profile, encryptedBackup);
        SaveProfiles(profiles);

        int succeeded = results.Count(r => r.success);
        int failedImp = results.Count(r => !r.success);
        Console.WriteLine($"Imported {succeeded} secret(s) into profile '{profileName}', {failedImp} failed");
        RecordProfileAudit("profile import-secrets", profileName,
            JsonSerializer.Serialize(new { file = inputFile, imported = succeeded, failed = failedImp }), null);
        return 0;
    }

    // Best-effort decrypt for the show-with-reveal path. Returns "<decryption-failed>"
    // on any exception (fail-closed UI signal: never echo ciphertext back to stdout).
    // v0.8.0: also resolves the v0.8 secret-mount envelope ("mount:" prefixed ID).
    static string TryDecryptSafe(string ciphertext)
    {
        try { return SecretProviderManager.Decrypt(ResolveSecretMount(ciphertext) ?? ciphertext); }
        catch { return "<decryption-failed>"; }
    }
}
