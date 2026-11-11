// AwsRegion.cs - the region-name check that keeps a request on AWS's own host
// License: Apache-2.0

using System;

namespace EnvManager.Secrets.Providers;

internal static class AwsRegion
{
    /// <summary>
    /// True for a plausible AWS region name: lowercase letters, digits and dashes.
    /// A region is interpolated into the request host, so anything outside this
    /// alphabet (a dot, an '@', an '/') could redirect the request - and the session
    /// token it carries - to another host. The check stays deliberately narrow: every
    /// real region name fits it.
    /// </summary>
    internal static bool IsName(string region)
    {
        if (region.Length == 0 || region.Length > 32) return false;
        foreach (char c in region)
        {
            if (c >= 'a' && c <= 'z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c == '-') continue;
            return false;
        }
        return true;
    }
}
