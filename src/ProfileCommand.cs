using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// Profile command domain (architecture-recovery issue 05): profile list/show/create/delete/apply/unapply,
/// variable/PATH editing, launch + preflight, secrets, secret-provider management, export/import, rename,
/// and the secret-provider CLI routing, moved verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
    // --- Profile commands ---
    // Mirrors PowerToys profile logic: profiles override user variables,
    // original values are backed up as name_PowerToys_<profileName> before apply,
    // and restored on unapply. Profiles only affect user-scope variables.

    internal static int RunProfileCommand(string[] args)
    {
        if (args.Length < 2)
        {
            ShowProfileHelp();
            return 0;
        }

        string sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "list" => ProfileList(),
            "create" => ProfileCreate(args),
            "delete" => args.Length < 3 ? ArgError("Usage: env-manager profile delete <name>") : ProfileDelete(args[2]),
            "apply" => args.Length < 3 ? ArgError("Usage: env-manager profile apply <name> [--strict]") : ProfileApply(args, args.Skip(2).FirstOrDefault(n => !n.StartsWith("--"))),
            "unapply" => args.Length < 3 ? ArgError("Usage: env-manager profile unapply <name>") : ProfileUnapply(args[2]),
            "show" => args.Length < 3 ? ArgError("Usage: env-manager profile show <name> [--reveal]") : ProfileShow(args[2], args.Any(a => a.Equals("--reveal", StringComparison.OrdinalIgnoreCase))),
            "preview" => args.Length < 3 ? ArgError("Usage: env-manager profile preview <name>") : ProfilePreview(args[2]),
            "set-inherits" => args.Length < 3 ? ArgError("Usage: env-manager profile set-inherits <name> [parent ...]") : ProfileSetInherits(args),
            "add-path" => ProfileAddPathWithScope(args),
            "remove-path" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-path <name> <directory>") : ProfileRemovePath(args[2], args[3]),
            "add-var" => ProfileAddVarWithScope(args),
            "remove-var" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-var <profile> <name>") : ProfileRemoveVar(args[2], args[3]),
            "edit-var" => args.Length < 6 ? ArgError("Usage: env-manager profile edit-var <profile> <old-name> <new-name> <new-value>") : ProfileEditVar(args[2], args[3], args[4], args[5]),
            "status" => args.Length < 3 ? ArgError("Usage: env-manager profile status <name>") : ProfileStatus(args[2]),
            "export" => ProfileExport(args),
            "import" => ProfileImport(args),
            "rename" => args.Length < 4 ? ArgError("Usage: env-manager profile rename <old> <new>") : ProfileRename(args[2], args[3]),
            // v0.6.0 Launch profile commands. Configure target executable + isolation settings
            // without writing anything to the registry. Spawn a child process with env_clear + inject.
            "set-launch" => ProfileSetLaunch(args),
            "launch" => args.Length < 3 ? ArgError("Usage: env-manager profile launch <name> [-- <extra-args ...>]") : ProfileLaunch(args),
            // v0.7.0 DPAO secrets subcommands.
            "add-secret" => args.Length < 5 ? ArgError("Usage: env-manager profile add-secret <profile> <name> <value>") : ProfileAddSecret(args[2], args[3], args[4]),
            "edit-secret" => args.Length < 6 ? ArgError("Usage: env-manager profile edit-secret <profile> <old> <new> <value>") : ProfileEditSecret(args[2], args[3], args[4], args[5]),
            "remove-secret" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-secret <profile> <name>") : ProfileRemoveSecret(args[2], args[3]),
            "reveal-secret" => args.Length < 4 ? ArgError("Usage: env-manager profile reveal-secret <profile> <name>") : ProfileRevealSecret(args[2], args[3]),
            // v0.8 secret export/import subcommands
            "export-secrets" => ProfileExportSecrets(args),
            "import-secrets" => ProfileImportSecrets(args),
            // v0.8 secret provider management subcommands
            "secret-provider" => RunSecretProviderCommand(args),
            "help" => ShowProfileHelp(),
            _ => ArgError($"Unknown profile subcommand: {sub}")
        };
    }

    /// <summary>
    /// Returns the backup variable name for a toggled (disabled) variable.
    /// Mirrors PowerToys: name + "_EnvManager_disabled"
    /// </summary>

    static int ProfileEditVar(string profileName, string oldVarName, string newVarName, string newVarValue)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        if (profile.IsEnabled)
            return ArgError("Error: Unapply the profile before changing its variables");

        var var = profile.Variables.FirstOrDefault(v => v.Name.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
        if (var == null)
        {
            Console.Error.WriteLine($"Error: Variable '{oldVarName}' not found in profile '{profileName}'");
            return 1;
        }

        // If name changed and profile is applied, handle backup rename
        if (!oldVarName.Equals(newVarName, StringComparison.OrdinalIgnoreCase) && profile.IsEnabled)
        {
            string oldBackupName = GetBackupVariableName(oldVarName, profileName);
            string newBackupName = GetBackupVariableName(newVarName, profileName);

            var oldBackup = GetVariableValue(oldBackupName, "user");
            if (oldBackup != null)
            {
                SetVariableWithoutNotify(newBackupName, oldBackup, "user");
                DeleteVariableWithoutNotify(oldBackupName, "user");
            }

            DeleteVariableWithoutNotify(oldVarName, "user");
        }

        var preEditVar = new ProfileVariable { Name = var.Name, Value = var.Value };
        var.Name = newVarName;
        var.Value = newVarValue;
        SaveProfiles(profiles);

        if (profile.IsEnabled)
        {
            SetVariableWithoutNotify(newVarName, newVarValue, "user");
            BroadcastSettingChange();
        }

        var postEditVar = new ProfileVariable { Name = newVarName, Value = newVarValue };
        RecordProfileAudit("profile edit-var", profileName, JsonSerializer.Serialize(preEditVar, JsonOpts), JsonSerializer.Serialize(postEditVar, JsonOpts));
        Console.WriteLine($"Edited variable '{oldVarName}' -> '{newVarName}' in profile '{profileName}'");
        return 0;
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


    static int ProfileStatus(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        bool correctlyApplied = profile.IsEnabled && IsProfileCorrectlyApplied(profile);
        bool applicable = IsProfileApplicable(profile);

        var result = new
        {
            name = profile.Name,
            isEnabled = profile.IsEnabled,
            isCorrectlyApplied = correctlyApplied,
            isApplicable = applicable,
            variableCount = profile.Variables.Count
        };
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
        return 0;
    }

    static int ProfileExport(string[] args)
    {
        if (args.Length < 5 || args[3] != "--output")
        {
            Console.Error.WriteLine("Usage: env-manager profile export <name> --output <file>");
            return 1;
        }

        string profileName = args[2];
        string outputPath = ValidateFilePath(args[4], mustExist: false);

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        var exportData = new
        {
            name = profile.Name,
            inherits = profile.Inherits,
            pathEntries = profile.PathEntries,
            variables = profile.Variables.Select(v => new { name = v.Name, value = v.Value }).ToList()
        };

        string json = JsonSerializer.Serialize(exportData, JsonOptsIndented);
        WriteAtomicUtf8(outputPath, json);
        Console.WriteLine($"Exported profile '{profileName}' to {outputPath}");
        return 0;
    }

    static int ProfileImport(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: env-manager profile import <file>");
            return 1;
        }

        string inputPath = ValidateFilePath(args[2], mustExist: true);

        string json = File.ReadAllText(inputPath);
        using var doc = JsonDocument.Parse(json);

        string profileName = doc.RootElement.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(profileName))
        {
            Console.Error.WriteLine("Error: Profile name is empty in import file");
            return 1;
        }

        var profiles = LoadProfiles();
        var existing = FindProfile(profiles, profileName);
        if (existing != null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' already exists. Delete it first or rename in the import file.");
            return 1;
        }

        var newProfile = new ProfileData
        {
            Id = Guid.NewGuid().ToString(),
            Name = profileName,
            IsEnabled = false
        };

        if (doc.RootElement.TryGetProperty("inherits", out var inheritsElement))
            newProfile.Inherits = inheritsElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();
        if (doc.RootElement.TryGetProperty("pathEntries", out var pathsElement))
            newProfile.PathEntries = pathsElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();

        foreach (var varElem in doc.RootElement.GetProperty("variables").EnumerateArray())
        {
            string varName = varElem.GetProperty("name").GetString() ?? "";
            string varValue = varElem.GetProperty("value").GetString() ?? "";
            if (!string.IsNullOrEmpty(varName))
            {
                newProfile.Variables.Add(new ProfileVariable { Name = varName, Value = varValue });
            }
        }

        profiles.Add(newProfile);
        SaveProfiles(profiles);
        Console.WriteLine($"Imported profile '{profileName}' with {newProfile.Variables.Count} variables");
        return 0;
    }

    static int ProfileRename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            Console.Error.WriteLine("Error: New profile name cannot be empty");
            return 1;
        }
        if (newName.Length > 255)
        {
            Console.Error.WriteLine("Error: Profile name exceeds 255 characters");
            return 1;
        }
        if (newName.Contains('\0') || newName.Contains('\n') || newName.Contains('\r'))
        {
            Console.Error.WriteLine("Error: Profile name contains invalid characters");
            return 1;
        }

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, oldName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{oldName}' not found");
            return 1;
        }

        // Check for name collision
        if (profiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && p.Id != profile.Id))
        {
            Console.Error.WriteLine($"Error: Profile '{newName}' already exists");
            return 1;
        }

        // If profile is applied, we need to handle backup key renames
        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled)
        {
            UnapplyProfile(profile);
        }

        string oldProfileName = profile.Name;
        profile.Name = newName;
        SaveProfiles(profiles);

        if (wasEnabled)
        {
            ApplyProfile(profile);
        }

        RecordProfileAudit("profile rename", newName, oldProfileName, newName);
        Console.WriteLine($"Renamed profile '{oldProfileName}' -> '{newName}'");
        return 0;
    }

        // v0.6.0: Launch profile support --------------------------------------------------
    // Configure (or reset) a Launch profile's target executable / args / cwd without
    // writing anything to the registry. The profile's variables remain the trusted source;
    // when `profile launch` runs we spawn the target with env_clear + inject.
    static int ProfileSetLaunch(string[] args)
    {
        // Usage: profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]
        // If [--type launch] is passed on an existing Global profile, we convert it (apply status must be false).
        // If [--target """] clears target (only valid when [--type global]) the profile is converted back.
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: env-manager profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]");
            return 1;
        }
        string name = args[2];
        string? target = null, launchArgs = null, cwd = null, newType = null;
        for (int i = 3; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--target": if (++i >= args.Length) goto Missing; target = args[i]; break;
                case "--args": if (++i >= args.Length) goto Missing; launchArgs = args[i]; break;
                case "--cwd": if (++i >= args.Length) goto Missing; cwd = args[i]; break;
                case "--type": if (++i >= args.Length) goto Missing; newType = args[i]; break;
                default: Console.Error.WriteLine($"Unknown flag: {a}"); return 1;
            }
        }
        Missing:
        if (target is null && newType is null)
        {
            Console.Error.WriteLine("Error: at least --target or --type must be specified");
            return 1;
        }
        if (newType is not null && !newType.Equals("global", StringComparison.OrdinalIgnoreCase) && !newType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --type must be 'global' or 'launch'");
            return 1;
        }

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled)
        {
            Console.Error.WriteLine($"Error: Cannot modify a launch configuration while profile '{name}' is applied. Unapply it first.");
            return 1;
        }

        if (newType is not null)
        {
            profile.ProfileType = newType.ToLowerInvariant();
            if (profile.ProfileType == "global") profile.TargetExecutable = null;
        }
        if (profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            if (target is not null) profile.TargetExecutable = StripVerbatimPrefix(target);
            if (launchArgs is not null) profile.LaunchArguments = launchArgs;
            if (cwd is not null) profile.WorkingDirectory = StripVerbatimPrefix(cwd);
            if (string.IsNullOrWhiteSpace(profile.TargetExecutable))
            {
                Console.Error.WriteLine("Error: Launch profile requires --target <exe>");
                return 1;
            }
        }
        else
        {
            // Global profile: --target is forbidden.
            if (target is not null)
            {
                Console.Error.WriteLine("Error: --target is only valid on Launch profiles");
                return 1;
            }
        }
        try
        {
            SaveProfiles(profiles);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine("Error: " + ScrubExceptionMessage(ex.Message));
            return 1;
        }
        Console.WriteLine($"Updated launch configuration for profile '{name}' (type={profile.ProfileType})");
        return 0;
    }

    /// <summary>
    /// Spawn the Launch profile's target executable with an isolated environment block.
    /// Critical safety properties:
    /// - Calls `Command(env_clear)` then `env(k,v)` for each profile variable + PATH entries.
    /// - NEVER calls `SetVariableWithoutNotify`, NEVER writes the registry, NEVER broadcasts WM_SETTINGCHANGE.
    /// - Secret names (in profile.SecretVariables) are read but their values are NOT echoed to stdout/stderr.
    /// - Logs only command names and arg counts (existing hard boundary: no env values in logs).
    /// </summary>
    /// <summary>
    /// Ticket 04 seam extraction: profile launch entry-point validation, returned as an
    /// error string (null = valid) so xUnit tests cover it without the registry or any
    /// process spawn. Message text reproduces the pre-seam stderr branches verbatim.
    /// </summary>
    internal static string? ValidateLaunchPreflight(ProfileData profile)
    {
        if (!profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
            return $"Profile '{profile.Name}' is a Global profile; only Launch profiles support `profile launch`";
        if (string.IsNullOrWhiteSpace(profile.TargetExecutable))
            return $"Profile '{profile.Name}' has no targetExecutable. Use 'profile set-launch {profile.Name} --target <exe>' first.";
        return null;
    }

    static int ProfileLaunch(string[] args)
    {
        string name = args[2];
        // Collect any trailing args after "--" and pass to the child as additional app args.
        List<string> extraArgs = new();
        int dashIndex = Array.IndexOf(args, "--");
        if (dashIndex >= 0) extraArgs = args.Skip(dashIndex + 1).ToList();

        // Ticket 19: --strict promotes pre-flight warnings to hard failures (exit 1).
        bool strict = args.Any(a => a.Equals("--strict", StringComparison.OrdinalIgnoreCase));
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{name}' not found"); return 1; }
        string? launchError = ValidateLaunchPreflight(profile);
        if (launchError != null) { Console.Error.WriteLine("Error: " + launchError); return 1; }
        // Ticket 19 warn tier: a dangling launch target is suspicious-but-safe locally
        // (the spawn itself fails loudly with exit 1); --strict refuses it up front.
        if (strict && !string.IsNullOrWhiteSpace(profile.TargetExecutable))
        {
            string full = Path.IsPathRooted(profile.TargetExecutable)
                ? profile.TargetExecutable
                : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, profile.TargetExecutable));
            if (!File.Exists(full))
            {
                var strictResult = new PreflightResult();
                strictResult.Warnings.Add($"Launch target does not exist: {profile.TargetExecutable} (dangling launch target)");
                EmitPreflightWarnReport("profile launch", name, strictResult, strict: true);
                return 1;
            }
        }

       string exe = profile.TargetExecutable!;
       string cwd = profile.WorkingDirectory ?? Path.GetDirectoryName(exe)!;
        // v0.9.3: If the stored working directory no longer exists (e.g. portable
        // app moved to a new version-named folder), fall back to the exe's directory
        // rather than crashing with "system cannot find the file specified".
        if (!string.IsNullOrEmpty(profile.WorkingDirectory) && !Directory.Exists(profile.WorkingDirectory))
        {
            string exeDir = Path.GetDirectoryName(exe) ?? "";
            if (Directory.Exists(exeDir)) cwd = exeDir;
        }
        else if (!Directory.Exists(cwd))
        {
            // Last resort: use the current working directory if neither exists.
            cwd = Environment.CurrentDirectory;
        }
       var effectiveVars = GetEffectiveProfileVariables(profile);
        var pathEntries = ResolveProfilePaths(profile);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = cwd,
        };
        // Critical: do NOT inherit the parent's environment. The whole point of a Launch profile
        // is per-app isolation. Start from an empty env block, then inject only profile vars.
        psi.EnvironmentVariables.Clear();
        // Inject PATH first so subsequent variables that reference %PATH% resolve cleanly.
        var currentPath = pathEntries.Count > 0
            ? string.Join(';', pathEntries)
            : string.Empty;
        if (!string.IsNullOrEmpty(currentPath)) psi.EnvironmentVariables["PATH"] = currentPath;
        foreach (var v in effectiveVars)
        {
            if (v.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase)) continue;
            // v0.7: If this variable is in the profile's SecretVariables list, the stored
            // value is a base64-encoded DPAPI ciphertext (CurrentUser scope). Decrypt here so
            // the child process receives plaintext in its env block. Plaintext lives only in
            // this launcher process memory; never written to disk, the registry, or logs.
           string valueToInject = v.Value ?? string.Empty;
           if (profile.SecretVariables.Contains(v.Name, StringComparer.OrdinalIgnoreCase))
           {
               try
               {
                    // v0.8.0: resolve mount reference if the value is a "mount:" prefixed ID.
                    valueToInject = ResolveSecretMount(valueToInject) ?? valueToInject;
                   valueToInject = SecretProviderManager.Decrypt(valueToInject, profile.Name + "\\" + v.Name);
               }
                catch (Exception)
                {
                    // Decryption failed: refuse to inject. Silent injection of garbage or
                    // ciphertext would be worse than failing the whole launch loudly.
                    Console.Error.WriteLine($"Error: Failed to decrypt secret '{v.Name}' for profile '{name}'");
                    return 1;
                }
            }
            psi.EnvironmentVariables[v.Name] = valueToInject;
        }
        foreach (string a in extraArgs) psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(profile.LaunchArguments))
        {
            // Append the profile-defined static args after the user-supplied extras are already listed.
            // Order: user-supplied first, then profile default args. (Avoid surprising cross-shell parse.)
            psi.ArgumentList.Add(profile.LaunchArguments);
        }

        try
        {
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) { Console.Error.WriteLine("Error: Failed to start child process"); return 1; }
            Console.WriteLine($"Launched '{exe}' (PID={proc.Id}) with isolated environment from profile '{name}'. {(extraArgs.Count > 0 ? "Extra args: " + extraArgs.Count : "")}");
            return 0;
        }
       catch (System.ComponentModel.Win32Exception ex)
       {
            string hint = Directory.Exists(cwd) ? "" : $" Working directory '{cwd}' does not exist.";
            Console.Error.WriteLine($"Error: Failed to launch '{exe}':{hint} {ScrubExceptionMessage(ex.Message)}");
           return 1;
       }
    }

    static int ShowProfileHelp()
    {
        Console.WriteLine(@"Profile commands:
  profile list                        List all profiles (JSON)
  profile create <name> [--type global|launch] [--target <exe>]
                                                    Create a Global or Launch profile atomically
  profile delete <name>               Delete a profile
  profile show <name>                 Show profile details (JSON)
  profile apply <name> [--strict]     Apply a profile (backs up existing user vars).
                                                      Preflight warnings (undefined %VAR%, stale PATH
                                                      entry, dangling launch target) are advisory: exit 2 on
                                                      success. --strict treats warnings as errors (exit 1).
  profile unapply <name>              Unapply a profile (restores backed-up user vars)
  profile add-var <profile> <name> <val> [--scope user|system]
                                                     Add a variable to a profile (v0.7.1: --scope routes to user or system on apply)
  profile add-path <profile> <dir> [--scope user|system]
                                                     Add a PATH entry to a profile (v0.7.1: --scope stored per-entry)
  profile remove-path <profile> <dir>               Remove a PATH entry from a profile
  profile remove-var <profile> <name>                Remove a variable from a profile
  profile edit-var <profile> <old> <new> <val>       Edit a variable in a profile
  profile status <name>                         Check profile application status
  profile export <name> --output <file>          Export profile to JSON file
  profile import <file>                          Import profile from JSON file
  profile rename <old> <new>                     Rename a profile
  profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]
  profile launch <name> [--strict] [-- <extra-args ...>]    Spawn target with isolated env (no registry write); --strict refuses a dangling launch target up front
  profile add-secret <profile> <name> <val>     Encrypt a value with DPAPI-CurrentUser; stored as ciphertext in profile json
  profile edit-secret <profile> <old> <new> <val>  Rename/re-encrypt a secret; plaintext lives only in memory
  profile remove-secret <profile> <name>          Remove a secret variable from a profile
  profile reveal-secret <profile> <name>          Print one secret's plaintext to stdout (DPAPI-bound to current user only)
  profile show <name> [--reveal]                  Show profile details (secret values masked unless --reveal; --reveal still only outputs decrypted value for the current user)
  profile export-secrets <profile> <file>          Export encrypted secrets from a profile to a file
  profile import-secrets <profile> <file>          Import encrypted secrets from a file into a profile
  profile secret-provider list                     List available secret providers and active selection
  profile secret-provider set <name>               Set the active secret provider
  profile secret-provider rotate                   Re-encrypt all secrets across all profiles with the active provider");
        return 0;
    }

    static ProfileData? FindProfile(List<ProfileData> profiles, string name)
    {
        var matches = profiles.Where(profile =>
            profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (matches.Count > 1)
            throw new InvalidDataException($"Ambiguous profile name '{name}'. Rename one profile before using name-only commands.");
        return matches.FirstOrDefault();
    }

    static int ProfileList()
    {
        var profiles = LoadProfiles();
        Console.WriteLine(JsonSerializer.Serialize(profiles, JsonOptsIndented));
        return 0;
    }

    static int ProfileCreate(string[] args)
    {
        if (args.Length < 3)
            return ArgError("Usage: env-manager profile create <name> [--type global|launch] [--target <exe>] [--args <args>] [--cwd <dir>]");

        string name = args[2];
        string profileType = "global";
        string? target = null;
        string? launchArgs = null;
        string? workingDirectory = null;
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--type": if (++i >= args.Length) return ArgError("Missing value for --type"); profileType = args[i]; break;
                case "--target": if (++i >= args.Length) return ArgError("Missing value for --target"); target = args[i]; break;
                case "--args": if (++i >= args.Length) return ArgError("Missing value for --args"); launchArgs = args[i]; break;
                case "--cwd": if (++i >= args.Length) return ArgError("Missing value for --cwd"); workingDirectory = args[i]; break;
                default: return ArgError($"Unknown flag: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            Console.Error.WriteLine("Error: Profile name must be 1-255 characters");
            return 1;
        }
        if (name.Contains('\0') || name.Contains('\n') || name.Contains('\r'))
        {
            Console.Error.WriteLine("Error: Profile name contains invalid characters");
            return 1;
        }
        if (!profileType.Equals("global", StringComparison.OrdinalIgnoreCase) && !profileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --type must be 'global' or 'launch'");
            return 1;
        }
        bool isLaunch = profileType.Equals("launch", StringComparison.OrdinalIgnoreCase);
        if (isLaunch && string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("Error: Launch profile requires --target <exe>");
            return 1;
        }
        if (!isLaunch && (target != null || launchArgs != null || workingDirectory != null))
        {
            Console.Error.WriteLine("Error: --target, --args, and --cwd are only valid for Launch profiles");
            return 1;
        }

        var profiles = LoadProfiles();
        if (FindProfile(profiles, name) != null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' already exists");
            return 1;
        }

        var profile = new ProfileData
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            IsEnabled = false,
            Variables = new List<ProfileVariable>(),
            ProfileType = isLaunch ? "launch" : "global",
            TargetExecutable = isLaunch ? StripVerbatimPrefix(target) : null,
            LaunchArguments = isLaunch ? launchArgs : null,
            WorkingDirectory = isLaunch ? StripVerbatimPrefix(workingDirectory) : null,
        };
        try
        {
            SaveProfiles(profiles.Append(profile).ToList());
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine("Error: " + error.Message);
            return 1;
        }
        RecordProfileAudit("profile create", name, null, ProfileSummary(profile));
        Console.WriteLine($"Created {profile.ProfileType} profile: {name}");
        return 0;
    }

    static int ProfileDelete(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        if (profile.IsEnabled)
        {
            UnapplyProfile(profile);
        }

        var deletedSummary = ProfileSummary(profile);
        profiles.Remove(profile);
        SaveProfiles(profiles);
        RecordProfileAudit("profile delete", name, deletedSummary, null);
        Console.WriteLine($"Deleted profile: {name}");
        return 0;
    }

    static int ProfileShow(string name, bool revealSecrets = false)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }
        // v0.7: secret variable values are DPAPI-encrypted ciphertext on disk. By default
        // we emit "<encrypted>" in place of their values via stdout, so that an agent
        // calling 'profile show' for structural inspection never inadvertently receives or
        // records plaintext. Use 'profile show <name> --reveal' to surface decrypted values
        // (DPAPI-bound to the current user; decryption fails on any other user account).
        var masked = new ProfileData
        {
            Id = profile.Id,
            Name = profile.Name,
            IsEnabled = profile.IsEnabled,
            AppliedAt = profile.AppliedAt,
            Inherits = profile.Inherits,
            PathEntries = profile.PathEntries,
            PathScopes = profile.PathScopes,
            ProfileType = profile.ProfileType,
            TargetExecutable = profile.TargetExecutable,
            LaunchArguments = profile.LaunchArguments,
            WorkingDirectory = profile.WorkingDirectory,
            SecretVariables = profile.SecretVariables,
        };
        // v0.9.16: Use ResolveProfileVariablesWithSource to populate Scope + SourceProfile for each variable.
        var resolvedVars = ResolveProfileVariablesWithSource(profile, profiles);
        foreach (var rv in resolvedVars)
        {
            if (profile.SecretVariables.Contains(rv.Name, StringComparer.OrdinalIgnoreCase))
            {
                masked.Variables.Add(new ProfileVariable
                {
                    Name = rv.Name,
                    Value = revealSecrets ? TryDecryptSafe(profile.Variables.First(pv => pv.Name.Equals(rv.Name, StringComparison.OrdinalIgnoreCase)).Value) : "<encrypted>",
                    Scope = rv.Scope,
                    SourceProfile = rv.SourceProfile,
                });
            }
            else
            {
                masked.Variables.Add(new ProfileVariable { Name = rv.Name, Value = rv.Value, Scope = rv.Scope, SourceProfile = rv.SourceProfile });
            }
        }
        // v0.9.16: Also include resolved PATH entries with sourceProfile for each inherited path.
        // v0.9.16: Also include resolved PATH entries with sourceProfile for each inherited path.
        masked.ResolvedPaths = ResolveProfilePathsWithSource(profile, profiles)
            .Select(p => new ResolvedPathEntry { Path = p.path, Scope = p.scope, SourceProfile = p.sourceProfile }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(masked, JsonOptsIndented));
        return 0;
    }

   static string TryDecryptSafe(string ciphertext)
   {
        // v0.8.0: resolve mount reference if the value is a "mount:" prefixed ID.
        try { return SecretProviderManager.Decrypt(ResolveSecretMount(ciphertext) ?? ciphertext); }
       catch { return "<decryption-failed>"; }
   }

    static int ProfileApply(string[] args, string? name)
    {
        // Ticket 19: --strict promotes pre-flight warnings to hard failures (exit 1).
        bool strict = args.Skip(2).Any(a => a.Equals("--strict", StringComparison.OrdinalIgnoreCase));
        var profiles = LoadProfiles();
        bool applyWarned = false;
        var profile = FindProfile(profiles, name ?? "");
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        // v0.7.1 hard boundary: Launch profiles are *local* only - they spawn a child
        // process with env_clear + inject and must NEVER be applied to the user
        // registry. Allowing apply would silently demote a Launch profile into a
        // Global-style persistent registry write, violating the locality contract
        // users rely on for variable isolation. Use `profile launch <name>` to
        // run the configured target with an isolated env block.
        if (profile.ProfileType != null &&
            profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Error: Profile '{name}' is a Launch (local) profile and cannot be applied to the registry. Use 'env-manager-cli profile launch <name>' to spawn its target with isolated variables.");
            return 1;
        }

        // T04-PREFLIGHT: same v0.7.7 boundary set, now exercised through the seam core
        // (RunProfilePreflight) so the inherited-secret rejection is unit-testable.
        // Ticket 19: two-tier validation - the error tier keeps the exact rejection copy;
        // the warn tier (undefined %VAR%, stale PATH entry, dangling launch target) is
        // advisory by default (exit 2 after a successful apply) and hard-fails under --strict.
        var preflight = RunProfilePreflightDetailed(profile, profiles, strict);
        if (preflight.HasErrors)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' contains invalid or protected variables and cannot be applied");
            foreach (string err in preflight.Errors) Console.Error.WriteLine("  - " + err);
            return 1;
        }
        if (preflight.HasWarnings)
        {
            EmitPreflightWarnReport("profile apply", name, preflight, strict);
            if (strict) return 1;
            applyWarned = true;
        }

        // Single active profile policy: unapply any currently-active profile before applying the new one.
        foreach (var other in profiles.Where(p => p.IsEnabled && p.Id != profile.Id).ToList())
        {
            UnapplyProfile(other);
            other.IsEnabled = false;
            other.AppliedAt = null;
            Console.WriteLine($"Unapplied profile: {other.Name} (single-profile policy)");
        }
        // If this profile is already applied, it's a no-op.
        if (profile.IsEnabled)
        {
            Console.WriteLine($"Profile '{name}' is already applied");
            return 0;
        }

        ApplyProfile(profile);
        profile.IsEnabled = true;
        profile.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            SaveProfiles(profiles);
        }
        catch
        {
            UnapplyProfile(profile);
            profile.IsEnabled = false;
            profile.AppliedAt = null;
            throw;
        }
        Console.WriteLine($"Applied profile: {name} ({profile.Variables.Count} variables)");
        // Ticket 19: warn-tier findings are advisory - the write went through, but the
        // process exit code records that the pre-flight had something to say (2 = warn).
        return applyWarned ? 2 : 0;
    }

    static int ProfileUnapply(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        if (!profile.IsEnabled)
        {
            Console.Error.WriteLine($"Warning: Profile '{name}' is not currently applied");
            return 0;
        }

        if (!CanUnapplySafely(profile, profiles))
            return ArgError("Error: A later-applied profile depends on overlapping variables; unapply it first");

        UnapplyProfile(profile);
        profile.IsEnabled = false;
        long? previousAppliedAt = profile.AppliedAt;
        profile.AppliedAt = null;
        try
        {
            SaveProfiles(profiles);
        }
        catch
        {
            ApplyProfile(profile);
            profile.IsEnabled = true;
            profile.AppliedAt = previousAppliedAt;
            throw;
        }
        Console.WriteLine($"Unapplied profile: {name}");
        return 0;
    }

    // Wrapper that parses optional --scope user|system from argv and delegates to
    // ProfileAddVar with the resolved scope. Mirrors the pattern used by other commands
    // that accept an optional --scope flag at the end of their argv vector.
    static int ProfileAddVarWithScope(string[] args)
    {
        if (args.Length < 5)
            return ArgError("Usage: env-manager profile add-var <profile> <name> <value> [--scope user|system]");

        string profileName = args[2];
        string varName = args[3];
        string varValue = args[4];
        string scope = "user";

        // Scan for --scope; skip and consume its value. Any extra token after the
        // value is rejected to keep behaviour predictable.
        for (int i = 5; i < args.Length; i++)
        {
            if (args[i].Equals("--scope", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                    return ArgError("Error: --scope requires a value (user or system)");
                scope = args[++i];
                if (scope != "user" && scope != "system")
                    return ArgError("Error: Invalid scope. Must be 'user' or 'system'");
            }
            else
            {
                return ArgError("Error: Unexpected argument: " + args[i]);
            }
        }

        return ProfileAddVar(profileName, varName, varValue, scope);
    }

    // Wrapper that parses optional --scope for add-path. Delegates to ProfileAddPath
    // with the resolved scope (default "user"). PATH entries tagged "system" are
    // stored with a scope attribute so ProfileApply can route them to HKLM.
    static int ProfileAddPathWithScope(string[] args)
    {
        if (args.Length < 4)
            return ArgError("Usage: env-manager profile add-path <profile> <directory> [--scope user|system]");

        string profileName = args[2];
        string path = args[3];
        string scope = "user";

        for (int i = 4; i < args.Length; i++)
        {
            if (args[i].Equals("--scope", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                    return ArgError("Error: --scope requires a value (user or system)");
                scope = args[++i];
                if (scope != "user" && scope != "system")
                    return ArgError("Error: Invalid scope. Must be 'user' or 'system'");
            }
            else
            {
                return ArgError("Error: Unexpected argument: " + args[i]);
            }
        }

        return ProfileAddPath(profileName, path, scope);
    }

    static int ProfileAddVar(string profileName, string varName, string varValue, string scope = "user")
    {
        if (string.IsNullOrWhiteSpace(varName) || varName.Length > 255 || varName.Contains('=') || ProtectedSystemVars.Contains(varName))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return 1;
        }
        if (scope != "user" && scope != "system")
        {
            Console.Error.WriteLine("Error: Invalid scope. Must be 'user' or 'system'");
            return 1;
        }
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        if (profile.IsEnabled)
            return ArgError("Error: Unapply the profile before changing its variables");

        profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        var addedVar = new ProfileVariable { Name = varName, Value = varValue, Scope = scope };
        profile.Variables.Add(addedVar);
        SaveProfiles(profiles);

        // If profile is currently applied, propagate the change to the registry
        // using the scope the user chose for this variable.
        if (profile.IsEnabled)
        {
            SetVariableWithoutNotify(varName, varValue, scope);
            BroadcastSettingChange();
        }

        RecordProfileAudit("profile add-var", profileName, null, JsonSerializer.Serialize(addedVar, JsonOpts));
        Console.WriteLine($"Added variable '{varName}' to profile '{profileName}'");
        return 0;
    }

    static int ProfileRemoveVar(string profileName, string varName)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        if (profile.IsEnabled)
            return ArgError("Error: Unapply the profile before changing its variables");

        var removedVar = profile.Variables.FirstOrDefault(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        int removed = profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            Console.Error.WriteLine($"Warning: Variable '{varName}' not found in profile '{profileName}'");
            return 0;
        }

        SaveProfiles(profiles);
        RecordProfileAudit("profile remove-var", profileName, JsonSerializer.Serialize(removedVar, JsonOpts), null);

        // If profile is currently applied, restore backup if it exists
        if (profile.IsEnabled)
        {
            string backupName = GetBackupVariableName(varName, profileName);
            var backupValue = GetVariableValue(backupName, "user");
            if (backupValue != null)
            {
                SetVariableWithoutNotify(varName, backupValue, "user");
                DeleteVariableWithoutNotify(backupName, "user");
            }
            else
            {
                DeleteVariableWithoutNotify(varName, "user");
            }
            BroadcastSettingChange();
        }

        Console.WriteLine($"Removed variable '{varName}' from profile '{profileName}'");
        return 0;
    }

    /// <summary>
    /// Returns the backup variable name for a given variable and profile.
    /// Mirrors PowerToys: name + "_PowerToys_" + profileName
    /// </summary>
    static string GetBackupVariableName(string varName, string profileName)
    {
        return varName + "_PowerToys_" + profileName;
    }

    /// <summary>

    // --- ProfileCommand members (architecture-recovery issue 06, moved verbatim from EnvFeatures.cs) ---

    /// <summary>
    /// Validates that a Launch profile target executable exists, has a known executable
    /// extension, and is NOT inside \\Windows\\System32 (hard refusal: system32 hijacking).
    /// </summary>
    static void ValidateLaunchTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidDataException("Launch target is empty");
        string cwd = Environment.CurrentDirectory;
        string full = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(cwd, target));
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".exe" or ".bat" or ".cmd" or ".ps1"))
            throw new InvalidDataException($"Launch target must be an .exe/.bat/.cmd/.ps1 file (got: {ext})");
        if (!File.Exists(full)) throw new InvalidDataException($"Launch target does not exist: {full}");
        // T04-SYS32-FIX: the prior verbatim literal @"c:\\windows\\system32\\" carries doubled separators,
        // so its compiled value never matches Path.GetFullPath output and the system32-hijacking
        // guard was inert. Match the resolved system folder (and a separator-normalized path) instead.
        string lower = full.ToLowerInvariant().Replace('/', '\\');
        string system32Prefix = Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd('\\').ToLowerInvariant() + '\\';
        if (lower.StartsWith(system32Prefix) || lower.StartsWith(@"\\windows\\system32\\"))
            throw new InvalidDataException("Launch targets inside \\Windows\\System32 are rejected to prevent system32 hijacking");
    }

    static List<ProfileVariable> ResolveProfileVariables(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new Dictionary<string, ProfileVariable>(StringComparer.OrdinalIgnoreCase);
        ResolveProfile(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        return result.Values.ToList();
    }

    static void ResolveProfile(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, Dictionary<string, ProfileVariable> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits)
        {
            var parent = FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName);
            ResolveProfile(parent, profiles, stack, result);
        }
        // T04-SCOPE-FIX: preserve Scope (default "user") so ApplyProfile routes system-scope
        // variables to the system store; the old projection silently reset Scope to "user".
        foreach (var variable in profile.Variables) result[variable.Name] = new ProfileVariable { Name = variable.Name, Value = variable.Value, Scope = variable.Scope };
        stack.Remove(profile.Name);
    }

    static List<string> ResolveProfilePaths(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new List<string>();
        ResolvePaths(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        return result.DistinctBy(NormalizePathEntry, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void ResolvePaths(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, List<string> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits) ResolvePaths(FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName), profiles, stack, result);
        result.AddRange(profile.PathEntries);
        stack.Remove(profile.Name);
    }

    // v0.9.15: Resolve PATH entries with per-entry scope preserved.
    // Returns (path, scope) pairs index-aligned with PathEntries[i] / PathScopes[i].
    // Missing PathScopes[i] defaults to "user" (backward compat with pre-v0.7.1 profiles).
    static List<(string path, string scope)> ResolveProfilePathsWithScopes(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new List<(string path, string scope)>();
        ResolvePathsWithScopes(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        // Deduplicate by normalized path entry, keeping first occurrence (which carries the most specific scope).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return result.Where(p =>
        {
            string norm = NormalizePathEntry(p.path);
            return seen.Add(norm);
        }).ToList();
    }

    static void ResolvePathsWithScopes(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, List<(string path, string scope)> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits) ResolvePathsWithScopes(FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName), profiles, stack, result);
        for (int i = 0; i < profile.PathEntries.Count; i++)
        {
            string scope = i < profile.PathScopes.Count ? (profile.PathScopes[i] ?? "user") : "user";
            result.Add((profile.PathEntries[i], scope));
        }
        stack.Remove(profile.Name);
    }

    // v0.9.16: Resolve PATH entries with per-entry scope AND source profile name preserved.
    // Returns (path, scope, sourceProfile) tuples tracking which profile contributed each entry.
    // Inherits chain walked depth-first; parent entries appear before child entries (same order as ResolveProfilePathsWithScopes).
    static List<(string path, string scope, string sourceProfile)> ResolveProfilePathsWithSource(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new List<(string path, string scope, string sourceProfile)>();
        ResolvePathsWithSource(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return result.Where(p =>
        {
            string norm = NormalizePathEntry(p.path);
            return seen.Add(norm);
        }).ToList();
    }

    static void ResolvePathsWithSource(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, List<(string path, string scope, string sourceProfile)> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits) ResolvePathsWithSource(FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName), profiles, stack, result);
        for (int i = 0; i < profile.PathEntries.Count; i++)
        {
            string scope = i < profile.PathScopes.Count ? (profile.PathScopes[i] ?? "user") : "user";
            result.Add((profile.PathEntries[i], scope, profile.Name));
        }
        stack.Remove(profile.Name);
    }

    // v0.9.16: Resolve variables with source profile name preserved.
    // Returns ProfileVariable list with Scope + SourceProfile fields populated.
    // Child variables override parent variables by name (same semantics as ResolveProfileVariables).
    static List<ProfileVariable> ResolveProfileVariablesWithSource(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new Dictionary<string, ProfileVariable>(StringComparer.OrdinalIgnoreCase);
        ResolveProfileWithSource(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        return result.Values.ToList();
    }

    static void ResolveProfileWithSource(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, Dictionary<string, ProfileVariable> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits)
        {
            var parent = FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName);
            ResolveProfileWithSource(parent, profiles, stack, result);
        }
        foreach (var variable in profile.Variables) result[variable.Name] = new ProfileVariable { Name = variable.Name, Value = variable.Value, Scope = variable.Scope, SourceProfile = profile.Name };
        stack.Remove(profile.Name);
    }

    static int ProfilePreview(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null) return ArgError("Error: Profile not found");
        var variables = ResolveProfileVariables(profile, profiles).Select(v => new { v.Name, v.Value, currentValue = GetVariableValue(v.Name, "user"), conflict = GetVariableValue(v.Name, "user") != null }).ToList();
        var paths = ResolveProfilePaths(profile, profiles).Select(path => new { path, expandedPath = Environment.ExpandEnvironmentVariables(path), exists = FastDirectoryExists(Environment.ExpandEnvironmentVariables(path)) }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(new { profile = name, profile.Inherits, variables, pathEntries = paths }, JsonOptsIndented));
        return 0;
    }

    // v0.7.5: DFS to detect if adding parents would close a cycle. Walk
// every requested parent's existing Inherits chain; if any chain leads back
// to the target profile name there is a cycle.
static bool HasInheritanceCycle(string targetName, List<string> requestedParents, List<ProfileData> allProfiles)
{
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stack = new Stack<string>(requestedParents);
    while (stack.Count > 0)
    {
        var cur = stack.Pop();
        if (cur.Equals(targetName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!visited.Add(cur)) continue;
        var p = allProfiles.FirstOrDefault(x => x.Name.Equals(cur, StringComparison.OrdinalIgnoreCase));
        if (p != null) foreach (var parent in p.Inherits) stack.Push(parent);
    }
    return false;
}

static int ProfileSetInherits(string[] args)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, args[2]);
        if (profile == null) return ArgError("Error: Profile not found");
        // v0.7.5: reject self-inheritance and cycles. A cycle (A inherits B
        // which inherits A) or a self-loop makes ResolveProfileVariables
        // infinite-loop and the profile un-recoverable.
        var requestedParents = args.Skip(3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requestedParents.Any(p => p.Equals(args[2], StringComparison.OrdinalIgnoreCase)))
            return ArgError("Error: A profile cannot inherit itself");
        if (HasInheritanceCycle(args[2], requestedParents, profiles))
            return ArgError("Error: Inheritance cycle detected. One of the requested parents already inherits (transitively) from '" + args[2] + "'.");
        // v0.7.7 hard boundary: a Global profile MUST NOT inherit from a Launch profile.
        // The Launch profile type carries DPAPI secrets that cannot be put in the user
        // registry as plaintext, and a Launch-targeted apply would never act on them
        // in the right scope. This is the same guard as IsProfileApplicable, applied
        // at set-inherits time so the user sees the rejection immediately instead of
        // only at apply time. We also forbid a Launch profile from inheriting another
        // Launch profile that carries secrets -- the inherited secret has no in-process
        // decrypt path in this profile.
        bool targetIsGlobal = profile.ProfileType.Equals("global", StringComparison.OrdinalIgnoreCase);
        bool targetIsLaunch = profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase);
        if (targetIsGlobal)
        {
            foreach (string parentName in requestedParents)
            {
                var parent = FindProfile(profiles, parentName);
                if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
                    return ArgError("Error: A Global profile cannot inherit from a Launch profile. Launch profiles may carry DPAPI secrets that would leak ciphertext to the user registry if inherited.");
            }
        }
        if (targetIsLaunch)
        {
            foreach (string parentName in requestedParents)
            {
                var parent = FindProfile(profiles, parentName);
                if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase)
                    && parent.SecretVariables.Count > 0)
                    return ArgError("Error: A Launch profile cannot inherit from another Launch profile that already carries secrets. The inherited secret has no in-process decrypt path in this profile's launch target.");
            }
        }
        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled) UnapplyProfile(profile);
        profile.Inherits = args.Skip(3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // v0.7.7: if the inheritance chain is somehow already poisoned (e.g. a
        // hand-edited profiles.json that bypassed CLI validation), ResolveProfile*
        // throws InvalidDataException. Wrap so set-inherits itself does not brick.
        try
        {
            ResolveProfileVariables(profile, profiles);
            ResolveProfilePaths(profile, profiles);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine("Error: Resolving the new inheritance chain failed: " + ex.Message + " -- the profiles.json file may have a pre-existing inheritance cycle. Aborting set-inherits without persisting.");
            return 1;
        }
        SaveProfiles(profiles);
        if (wasEnabled)
        {
            if (IsProfileApplicable(profile)) { ApplyProfile(profile); profile.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveProfiles(profiles); }
            else
            {
                profile.IsEnabled = false;
                SaveProfiles(profiles);
                Console.Error.WriteLine("Warning: Profile '" + profile.Name + "' is no longer applicable after the inheritance change (e.g. it now pulls in a secret variable). It has been disabled; fix the inheritance chain before re-applying.");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { profile = profile.Name, profile.Inherits }, JsonOpts));
        return 0;
    }

    static int ProfileAddPath(string profileName, string path, string scope = "user")
    {
        if (scope != "user" && scope != "system")
            return ArgError("Error: Invalid scope. Must be 'user' or 'system'");
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) return ArgError("Error: Profile not found");
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing PATH entries");
        ValidatePathFragment(path);
        if (!profile.PathEntries.Any(p => NormalizePathEntry(p).Equals(NormalizePathEntry(path), StringComparison.OrdinalIgnoreCase)))
        {
            profile.PathEntries.Add(path);
            // Track the scope the user chose for this entry. The list is parallel to
            // PathEntries; older profiles.json files without PathScopes are treated
            // as "user" by ProfileApply (index-based lookup with out-of-range guard).
            while (profile.PathScopes.Count < profile.PathEntries.Count - 1) profile.PathScopes.Add("user");
            profile.PathScopes.Add(scope);
        }
        SaveProfiles(profiles);
        Console.WriteLine("Added PATH entry to profile: " + profileName);
        return 0;
    }

    static int ProfileRemovePath(string profileName, string path)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) return ArgError("Error: Profile not found");
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing PATH entries");
        int idx = profile.PathEntries.FindIndex(p => NormalizePathEntry(p).Equals(NormalizePathEntry(path), StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            profile.PathEntries.RemoveAt(idx);
            // Keep PathScopes in lockstep with PathEntries by index. If PathScopes
            // was shorter (legacy profile), simply drop the matching tail entry.
            if (idx < profile.PathScopes.Count) profile.PathScopes.RemoveAt(idx);
        }
        SaveProfiles(profiles);
        Console.WriteLine("Removed PATH entry from profile: " + profileName);
        return 0;
    }

    static void ValidatePathFragment(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength || path.Contains(';') || path.Any(ch => ch == '\0' || (char.IsControl(ch) && ch != '\t'))) throw new ArgumentException("Invalid PATH entry");
    }

}
