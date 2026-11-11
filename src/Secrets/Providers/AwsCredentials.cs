// AwsCredentials.cs - credentials for one signed AWS request
// License: Apache-2.0

using System;

namespace EnvManager.Secrets.Providers;

/// <summary>Credentials for one signed request: a long-lived access key or a session.</summary>
internal readonly record struct AwsCredentials(string AccessKeyId, string SecretAccessKey, string? SessionToken)
{
    internal string Scope(string dateStamp, string region, string service) =>
        dateStamp + "/" + region + "/" + service + "/aws4_request";
}
