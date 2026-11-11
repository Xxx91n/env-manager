using EnvManager.Secrets.Core;
// AwsSecretTarget.cs - the `region|secretId` envelope payload and its request bodies
// License: Apache-2.0

// aws-secretsmanager and aws-roles-anywhere name the same remote Secrets Manager
// secrets. Both envelope payload shapes and both request bodies were written out twice;
// this is the single copy, so a change to the wire format cannot land in one provider
// and miss the other.

using System;
using System.Text;

namespace EnvManager.Secrets.Providers;

/// <summary>The remote secret an envelope points at.</summary>
internal readonly record struct AwsSecretTarget(string Region, string SecretId)
{
    /// <summary>`region|secretId`, the envelope's targetName.</summary>
    internal string EnvelopeValue => Region + "|" + SecretId;

    /// <summary>Null when the string is not a well-formed `region|secretId` pair.</summary>
    internal static AwsSecretTarget? Parse(string? targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;
        string[] parts = targetName.Split('|');
        if (parts.Length < 2) return null;
        // The region ends up inside the request URL, so it is restricted to AWS's own
        // region alphabet. Without this, `evil.com/@host` in an envelope would build
        // https://secretsmanager.evil.com/... and hand the session token to that host.
        // An explicit endpoint override builds the URL instead, so the region never
        // reaches a host name there and a non-AWS label such as `local_test` is allowed -
        // rejecting it would let Encrypt write an envelope that Decrypt then refuses. The
        // scope-label rule still bans the characters that would break the signature.
        bool regionOk = AwsEndpointOverride.SecretsManagerConfigured
            ? AwsRegion.IsSafeScopeLabel(parts[0])
            : AwsRegion.IsName(parts[0]);
        if (!regionOk) return null;
        if (parts[1].Length == 0) return null;
        return new AwsSecretTarget(parts[0], parts[1]);
    }

    internal static string NewSecretId() => "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

    /// <summary>AWS accepts these characters in a secret name; anything else becomes a dash.</summary>
    internal static string SanitizeSecretId(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) { if (char.IsLetterOrDigit(c) || "/_+=.@-".IndexOf(c) >= 0) sb.Append(c); else sb.Append('-'); }
        string r = sb.ToString();
        return r.Length > 512 ? r.Substring(0, 512) : r;
    }
}

/// <summary>The four Secrets Manager request bodies both providers send.</summary>
internal static class AwsSecretsManagerPayload
{
    // ClientRequestToken is the AWS-recommended idempotency token; the L1 LocalStack
    // lane rejects CreateSecret without it.
    internal static string CreateSecret(string secretId, string plaintext, string clientRequestToken) =>
        "{\"Name\":\"" + Escape(secretId) + "\",\"SecretString\":\"" + Escape(plaintext) +
        "\",\"ClientRequestToken\":\"" + Escape(clientRequestToken) + "\"}";

    internal static string GetSecretValue(string secretId) =>
        "{\"SecretId\":\"" + Escape(secretId) + "\"}";

    internal static string DeleteSecret(string secretId) =>
        "{\"SecretId\":\"" + Escape(secretId) + "\",\"ForceDeleteWithoutRecovery\":true}";

    internal static string PutSecretValue(string secretId, string plaintext) =>
        "{\"SecretId\":\"" + Escape(secretId) + "\",\"SecretString\":\"" + Escape(plaintext) + "\"}";

    internal static string Escape(string s) => AwsJson.Escape(s);
}
