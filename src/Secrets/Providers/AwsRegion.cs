// AwsRegion.cs - the region-name checks that keep a request on the intended host
// License: Apache-2.0

// Two checks, because a region plays two roles:
//   - without an endpoint override it is interpolated into the request host, so it has
//     to look like an AWS region name;
//   - with an override the host comes from the variable and the region only labels the
//     credential scope, so a local emulator may use a name like `local_test`.
// Both roles still reject the characters that could break out of a host name or a
// signature scope, which is what a hostile envelope would need.

namespace EnvManager.Secrets.Providers;

internal static class AwsRegion
{
    /// <summary>
    /// True for a plausible AWS region name: lowercase letters, digits and dashes only.
    /// Every real region fits; anything else could redirect a request built from it.
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

    /// <summary>
    /// True when the value is safe as a credential-scope label: no host separators
    /// (`@ / : \ ? #`), no whitespace, no control characters. Used when an endpoint
    /// override supplies the host, so an emulator label such as `local_test` is fine.
    /// </summary>
    internal static bool IsSafeScopeLabel(string region)
    {
        if (region.Length == 0 || region.Length > 32) return false;
        foreach (char c in region)
        {
            if (char.IsControl(c)) return false;
            if (char.IsWhiteSpace(c)) return false;
            if ("@/:\\?#|".IndexOf(c) >= 0) return false;
        }
        return true;
    }
}
