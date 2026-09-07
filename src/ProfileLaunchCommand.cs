using System.Diagnostics;
using System.Text;

namespace EnvManager;

/// <summary>
/// Profile launch sub-domain (architecture-recovery issue 26, ticket 26 split).
/// Extracted from ProfileCommand.cs: ProfileSetLaunch / ProfileLaunch / ValidateLaunchPreflight /
/// ValidateLaunchTarget. Behaviour is verbatim from the original 1605-line monolith; the
/// splitting rationale is per Q5 ticket 26 ("launch/secret-provider first, then CRUD").
///
/// Critical safety properties preserved verbatim:
/// - `profile launch` spawns the child with env_clear + env(k,v) injection; never writes
///   to the registry, never broadcasts WM_SETTINGCHANGE.
/// - ValidateLaunchTarget refuses \Windows\System32 (T04-SYS32-FIX) and non-executable
///   extensions; this lives in the launch sub-domain because only the launch path consumes it.
/// - Secret names (profile.SecretVariables) decrypt in this launcher process only;
///   plaintext lives in transient process memory.
/// </summary>
partial class Program
{
    // Dispatch entry point installed by RunProfileCommand in ProfileCommand.cs.
    // Routes both launch verbs (set-launch + launch) into their dedicated handlers.
    internal static int RunProfileLaunchCommand(string[] args)
    {
        string sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "set-launch" => ProfileSetLaunch(args),
            "launch" => args.Length < 3 ? ArgError("Usage: env-manager profile launch <name> [-- <extra-args ...>]") : ProfileLaunch(args),
            _ => ArgError($"Unknown profile launch subcommand: {sub}")
        };
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
    /// Kept internal static so ProfileSeamValidationTests can exercise it via
    /// Program.ValidateLaunchPreflight (ticket 32 zero-drift invariant).
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
}
