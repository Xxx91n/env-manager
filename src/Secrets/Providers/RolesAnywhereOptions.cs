using EnvManager.Secrets.Core;
// RolesAnywhereOptions.cs - the Roles Anywhere identity and requested session length
// License: Apache-2.0

using System;

namespace EnvManager.Secrets.Providers;

/// <summary>Where a Roles Anywhere identity comes from, plus the session length asked for.</summary>
internal sealed record RolesAnywhereOptions(
    string Region,
    string RoleArn,
    string ProfileArn,
    string TrustAnchorArn,
    string CertificateSha1,
    int DurationSeconds)
{
    /// <summary>Identity key for the session cache: a change to any part must refetch.</summary>
    internal string CacheKey =>
        Region + "|" + RoleArn + "|" + ProfileArn + "|" + TrustAnchorArn + "|" +
        CertificateSha1.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() + "|" + DurationSeconds;
}
