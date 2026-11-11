// AwsEndpointOverride.cs - the one place both AWS providers read their endpoint override
// License: Apache-2.0

// AWS_ENDPOINT_URL_SECRETS_MANAGER is the official service-specific override convention.
// Production never sets it; the L1 LocalStack lane does. Reading it in one place keeps the
// write path and the read path agreeing on whether a non-AWS region is acceptable.

using System;

namespace EnvManager.Secrets.Providers;

internal static class AwsEndpointOverride
{
    internal static string? SecretsManager => Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER") is { Length: > 0 } value ? value : null;

    /// <summary>
    /// True when requests do not go to an AWS-built host, so a region only labels the
    /// credential scope and never reaches a host name.
    /// </summary>
    internal static bool SecretsManagerConfigured => SecretsManager != null;
}
