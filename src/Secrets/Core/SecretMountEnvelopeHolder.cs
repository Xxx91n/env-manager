// SecretMountEnvelopeHolder.cs - the SecretMount side of ISecretEnvelopeHolder
// A profile references a mount by "mount:<id>"; the mount owns the envelope
// (SecretMount.Envelope), so rotation reads the envelope from the mount and writes the
// rotated one back into the same record. The holder mutates the caller's mount list in
// place and records whether anything changed: persisting secretMount.json belongs to the
// caller, because the file lives in the CLI's user-state store, not in this aggregate.
// License: Apache-2.0

using System;
using System.Collections.Generic;

namespace EnvManager.Secrets.Core;

internal sealed class SecretMountEnvelopeHolder : ISecretEnvelopeHolder
{
    // The prefix a profile uses to name a mount instead of holding an envelope. Written by
    // SecretMount.MigrateSecretsToMounts; read back by Program.ResolveSecretMount, by the
    // schema version detector in SchemaMigration, and by this holder. The writer only ever
    // emits this lowercase form, so the match here is ordinal - a lenient match would rotate
    // a secret that no reveal path can then open. The other two readers use the default
    // (culture) comparison; all three accept only the lowercase spelling.
    internal const string ReferencePrefix = "mount:";

    readonly List<SecretMount> _mounts;

    internal SecretMountEnvelopeHolder(List<SecretMount> mounts)
    {
        _mounts = mounts ?? throw new ArgumentNullException(nameof(mounts));
    }

    // True once a rotated envelope has been written into the list, so the caller knows the
    // mount file has to be rewritten.
    internal bool Changed { get; private set; }

    public bool IsReference(string storedValue) => TryMountId(storedValue, out _);

    public string ResolveReference(string storedValue)
    {
        string mountId = RequireMountId(storedValue);
        var mount = Find(mountId);
        if (mount == null)
        {
            // No mount record means no provider is knowable, so the provider slot names the
            // mount layer itself instead of inventing one; the mount id rides along as the
            // exception's MountId.
            throw new SecretProviderNotFoundException(
                "secret-mount", SecretProviderErrors.OpRotate,
                "A profile secret references mount '" + mountId + "', which is not in secretMount.json; " +
                "the secret was left untouched. Restore the mount or re-add the secret.", mountId);
        }
        if (string.IsNullOrEmpty(mount.Envelope))
        {
            // A mount whose envelope lives in the provider backend (Periodic) has no
            // CLI-side rotation path yet: nothing on disk to decrypt and re-encrypt. Counted
            // as a failure rather than skipped, because the secret really was not rotated.
            throw new SecretProviderNotFoundException(
                mount.Provider, SecretProviderErrors.OpRotate,
                "Mount '" + mountId + "' carries no envelope to rotate; the secret was left untouched.", mountId);
        }
        return mount.Envelope;
    }

    public void CommitReference(string storedValue, string rotatedEnvelope)
    {
        string mountId = RequireMountId(storedValue);
        var mount = Find(mountId)
            ?? throw new SecretProviderNotFoundException(
                "secret-mount", SecretProviderErrors.OpRotate,
                "Mount '" + mountId + "' disappeared while its secret was being rotated.", mountId);
        mount.Envelope = rotatedEnvelope;
        mount.LastRotatedAt = DateTimeOffset.UtcNow.ToString("O");
        Changed = true;
    }

    // The id inside a reference. A value that is not a reference is a caller bug, and the
    // message must not quote the value: for an inline secret that string is the ciphertext.
    static string RequireMountId(string storedValue)
        => TryMountId(storedValue, out string mountId)
            ? mountId
            : throw new ArgumentException("Value is not a secret mount reference", nameof(storedValue));

    static bool TryMountId(string storedValue, out string mountId)
    {
        if (!string.IsNullOrEmpty(storedValue)
            && storedValue.StartsWith(ReferencePrefix, StringComparison.Ordinal))
        {
            mountId = storedValue.Substring(ReferencePrefix.Length);
            return true;
        }
        mountId = "";
        return false;
    }

    SecretMount? Find(string mountId)
    {
        foreach (var mount in _mounts)
        {
            if (mount.Id.Equals(mountId, StringComparison.OrdinalIgnoreCase)) return mount;
        }
        return null;
    }
}
