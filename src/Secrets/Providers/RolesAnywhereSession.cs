// RolesAnywhereSession.cs - temporary credentials plus their expiry
// License: Apache-2.0

using System;

namespace EnvManager.Secrets.Providers;

/// <summary>Temporary credentials plus the moment they stop being usable.</summary>
internal sealed record RolesAnywhereSession(AwsCredentials Credentials, DateTimeOffset ExpiresAt)
{
    // Refresh this far ahead of expiry so a long call cannot outlive its credentials.
    internal static readonly TimeSpan RefreshLead = TimeSpan.FromMinutes(5);

    internal bool NeedsRefresh(DateTimeOffset now) => now >= ExpiresAt - RefreshLead;
}
