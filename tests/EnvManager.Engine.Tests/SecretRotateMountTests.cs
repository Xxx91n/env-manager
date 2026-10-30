// SecretRotateMountTests.cs - rotation across both stored secret forms
// License: Apache-2.0
//
// A profile's secret variable holds either the provider envelope itself (what the secret
// verbs write) or a "mount:<id>" reference into secretMount.json (what the schema migration
// produces for pre-existing secrets). Rotation has to handle both: the reference form keeps
// its envelope in the SecretMount aggregate, so a pass that only understands inline
// envelopes skips exactly the migrated secrets and reports them as provider failures -
// "0 helper spawns" for a secret that never reached a provider at all.
//
// Every test drives the real CLI verb (Program.RunProfileSecretCommand) against a redirected
// user-state root with the local DPAPI provider: no registry, no network, no subprocess.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using EnvManager;
using EnvManager.Secrets.Core;
using EnvManager.Secrets.Manager;
using EnvManager.Secrets.Providers;

using Xunit;

namespace EnvManager.Engine.Tests;

// Serial: redirects ENVMANAGER_LOCALAPPDATA (process-scoped) and captures the console -
// same discipline as LocalAppDataRedirectTests and the CLI snapshot suite.
[Collection("LocalAppDataRedirectSerial")]
public sealed class SecretRotateMountTests : IDisposable
{
    readonly string _root;
    readonly string? _previousRoot;

    public SecretRotateMountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "em-rotate-mount-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "EnvManager"));
        _previousRoot = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
        Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", _previousRoot);
        try { Directory.Delete(_root, true); } catch { /* temp isolation dir */ }
    }

    // ---- the defect this file pins ---------------------------------------------

    /// <summary>
    /// The reported symptom: a secret stored as a mount reference is rotated, and the new
    /// envelope lands in the mount that owns it - not in the profile, which keeps naming
    /// the mount.
    /// </summary>
    [Fact]
    public void Rotate_ResolvesMountReference_AndWritesTheEnvelopeBackToTheMount()
    {
        string context = @"p\M1";
        string originalEnvelope = NewEnvelope("mount-secret-1", context);
        SeedProfile("p", ("M1", "mount:m1"));
        SeedMounts(new SecretMount
        {
            Id = "m1",
            Provider = "dpapi-current-user",
            Name = "M1",
            Envelope = originalEnvelope,
            ProfileName = "p",
        });

        var (code, stdout) = RunRotate();

        Assert.Equal(0, code);
        Assert.Contains("Rotation complete: 1/1 secrets re-encrypted, 0 failed", stdout);

        var mount = Assert.Single(ReadMounts());
        Assert.NotEqual(originalEnvelope, mount.Envelope);
        Assert.Equal("mount-secret-1", SecretProviderManager.Decrypt(mount.Envelope!, context));
        Assert.False(string.IsNullOrEmpty(mount.LastRotatedAt),
            "a rotation of a mount-referenced secret must be recorded on that mount");
        // Ownership: the mount holds the envelope, the profile holds only the reference.
        Assert.Equal("mount:m1", Assert.Single(ReadProfiles()[0].Variables).Value);
    }

    /// <summary>Both forms in one profile: one pass must rotate the inline envelope in place
    /// and the referenced one in its mount.</summary>
    [Fact]
    public void Rotate_HandlesInlineAndMountReferencedSecretsInOnePass()
    {
        string inlineEnvelope = NewEnvelope("inline-secret", @"p\A");
        string mountEnvelope = NewEnvelope("mount-secret", @"p\B");
        SeedProfile("p", ("A", inlineEnvelope), ("B", "mount:m1"));
        SeedMounts(new SecretMount
        {
            Id = "m1",
            Provider = "dpapi-current-user",
            Name = "B",
            Envelope = mountEnvelope,
            ProfileName = "p",
        });

        var (code, stdout) = RunRotate();

        Assert.Equal(0, code);
        Assert.Contains("Rotation complete: 2/2 secrets re-encrypted, 0 failed", stdout);

        var variables = ReadProfiles()[0].Variables;
        Assert.NotEqual(inlineEnvelope, variables[0].Value);
        Assert.Equal("inline-secret", SecretProviderManager.Decrypt(variables[0].Value, @"p\A"));
        Assert.Equal("mount:m1", variables[1].Value);

        var mount = Assert.Single(ReadMounts());
        Assert.Equal("mount-secret", SecretProviderManager.Decrypt(mount.Envelope!, @"p\B"));
    }

    /// <summary>
    /// A reference with no mount behind it is a real failure, so it is counted - and the
    /// skip-and-count invariant still holds: the secret is neither deleted nor rewritten.
    /// </summary>
    [Fact]
    public void Rotate_CountsADanglingMountReferenceAsFailed_AndLeavesTheSecretInPlace()
    {
        string inlineEnvelope = NewEnvelope("inline-secret", @"p\A");
        SeedProfile("p", ("A", inlineEnvelope), ("GONE", "mount:missing"));

        var (code, stdout) = RunRotate();

        Assert.Equal(0, code);
        Assert.Contains("Rotation complete: 1/2 secrets re-encrypted, 1 failed", stdout);
        var variables = ReadProfiles()[0].Variables;
        Assert.Equal("mount:missing", variables[1].Value);
    }

    /// <summary>
    /// A rotate pass writes the mount file, so it must refuse one it cannot read: the plain
    /// loader silently falls back to the `.bak` copy, and persisting that stale snapshot
    /// would drop every mount created since the backup - leaving profiles whose `mount:<id>`
    /// references name ids that no longer exist.
    /// </summary>
    [Fact]
    public void Rotate_RefusesToRunOverAnUnparseableMountFile()
    {
        string inlineEnvelope = NewEnvelope("inline-secret", @"p\A");
        SeedProfile("p", ("A", inlineEnvelope), ("M", "mount:m1"));
        const string corrupt = "{ this is not a mount list";
        File.WriteAllText(MountsPath, corrupt);
        File.WriteAllText(MountsPath + ".bak", JsonSerializer.Serialize(new[]
        {
            new SecretMount { Id = "m1", Provider = "dpapi-current-user", Name = "M", Envelope = inlineEnvelope },
        }, Indented));

        var (code, _) = RunRotate();

        Assert.Equal(1, code);
        Assert.Equal(corrupt, File.ReadAllText(MountsPath));
        Assert.Equal(inlineEnvelope, ReadProfiles()[0].Variables[0].Value);
    }

    /// <summary>
    /// End to end over the CLI verbs, reproducing the sequence that produced the defect: a
    /// fresh profile, several `add-secret` calls (the second one runs the mount migration
    /// over the first secret), then `rotate`. Every secret must be rotated - the migrated
    /// one included - and every secret must still reveal its own plaintext afterwards.
    /// </summary>
    [Fact]
    public void AddingSecretsThenRotating_RotatesEverySecret()
    {
        SeedProfileWithoutSecrets("p");
        for (int i = 1; i <= 3; i++)
        {
            Assert.Equal(0, Run("profile", "add-secret", "p", "S" + i, "plain-" + i).code);
        }
        Assert.True(File.Exists(MountsPath),
            "the second add-secret migrates the first inline envelope into a mount; without "
            + "that the fixture does not reproduce the mixed storage the defect lives in");

        var (code, stdout) = RunRotate();

        Assert.Equal(0, code);
        Assert.Contains("Rotation complete: 3/3 secrets re-encrypted, 0 failed", stdout);
        for (int i = 1; i <= 3; i++)
        {
            var reveal = Run("profile", "reveal-secret", "p", "S" + i);
            Assert.Equal(0, reveal.code);
            Assert.Equal("plain-" + i, reveal.stdout);
        }
    }

    // ---- fixtures --------------------------------------------------------------

    string StateDir => Path.Combine(_root, "EnvManager");
    string MountsPath => Path.Combine(StateDir, "secretMount.json");

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // The launch target only has to exist and stay out of \Windows\System32 (the CLI refuses
    // that outright); the test host binary satisfies both without staging anything.
    static string LaunchTarget()
    {
        string? path = Environment.ProcessPath;
        Assert.False(string.IsNullOrEmpty(path), "the test host must expose a process path");
        return path!;
    }

    static string NewEnvelope(string plaintext, string context)
        => new DpapiCurrentUserProvider().Encrypt(plaintext, context);

    static ProfileData MkLaunchProfile(string name, params (string name, string value)[] variables)
    {
        var profile = new ProfileData();
        profile.SetName(name);
        profile.SetProfileType("launch");
        profile.SetLaunchTarget(LaunchTarget());
        profile.SetInherits(new List<string>());
        profile.SetPathEntries(new List<string>());
        foreach (var (varName, value) in variables) profile.AddVariable(varName, value);
        profile.SetSecretVariables(variables.Select(v => v.name).ToList());
        // Stamped at the current schema so loading does not run the migration and rewrite the
        // fixture shape these tests set up on purpose.
        profile.SetSchemaVersion(2);
        return profile;
    }

    /// <summary>Seed a profile that carries the given secret variables, stored verbatim.</summary>
    void SeedProfile(string name, params (string name, string value)[] variables)
        => Program.SaveProfilesRawForTests(new List<ProfileData> { MkLaunchProfile(name, variables) });

    /// <summary>Seed an un-stamped launch profile with no secrets, so the CLI verbs perform
    /// the migration themselves (the shape the defect needs).</summary>
    void SeedProfileWithoutSecrets(string name)
    {
        var profile = MkLaunchProfile(name);
        profile.SetSchemaVersion(0);
        Program.SaveProfilesRawForTests(new List<ProfileData> { profile });
    }

    void SeedMounts(params SecretMount[] mounts)
        => File.WriteAllText(MountsPath, JsonSerializer.Serialize(mounts, Indented));

    List<ProfileData> ReadProfiles() => Program.LoadProfiles();

    List<SecretMount> ReadMounts() =>
        JsonSerializer.Deserialize<List<SecretMount>>(File.ReadAllText(MountsPath)) ?? new();

    (int code, string stdout) RunRotate() => Run("profile", "secret-provider", "rotate");

    static (int code, string stdout) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int code = Program.RunProfileSecretCommand(args);
            return (code, stdout.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }
}
