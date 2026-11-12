// AwsEndpointOverride.cs - the one place both AWS providers read their endpoint override
// License: Apache-2.0

// AWS_ENDPOINT_URL_SECRETS_MANAGER is the official service-specific override convention.
// Production never sets it; the L1 LocalStack lane does. Reading it in one place keeps the
// write path and the read path agreeing on whether a non-AWS region is acceptable.

using System;

using EnvManager.Secrets.Core;

namespace EnvManager.Secrets.Providers;

internal static class AwsEndpointOverride
{
    internal const string VariableName = "AWS_ENDPOINT_URL_SECRETS_MANAGER";

    internal static string? SecretsManager => Environment.GetEnvironmentVariable(VariableName) is { Length: > 0 } value ? value : null;

    /// <summary>
    /// True when requests do not go to an AWS-built host, so a region only labels the
    /// credential scope and never reaches a host name.
    /// </summary>
    internal static bool SecretsManagerConfigured => SecretsManager != null;

    /// <summary>
    /// Checks an endpoint override before a request is built. https:// is fine anywhere;
    /// http:// is fine only for a host on this machine, because a remote host would get the
    /// signed request and its session token in clear text. The address is not echoed into
    /// the error, so a value carrying credentials in it cannot reach a log.
    /// </summary>
    internal static string? RequireSafeEndpoint(string provider, string operation, string? endpointOverride)
    {
        if (string.IsNullOrEmpty(endpointOverride)) return null;

        if (!Uri.TryCreate(endpointOverride, UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new SecretProviderPermissionDeniedException(provider, operation,
                VariableName + " must be an absolute http:// or https:// address");
        }

        if (endpoint.Scheme == Uri.UriSchemeHttp && !endpoint.IsLoopback)
        {
            throw new SecretProviderPermissionDeniedException(provider, operation,
                VariableName + " may use http:// only for a host on this machine (the loopback range, " +
                "localhost, [::1]); every other host needs https://");
        }

        return endpointOverride;
    }
}
