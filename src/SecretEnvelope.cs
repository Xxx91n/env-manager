// SecretEnvelope.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

internal sealed class SecretEnvelope
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "dpapi-current-user";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    // For dpapi-current-user: base64 DPAPI blob
    // For credential-manager: CRED target name (e.g. "EnvManager\\<profile>\\<var>")
    [JsonPropertyName("ciphertext")]
    public string? Ciphertext { get; set; }

    // For credential-manager: the actual encrypted credential blob is in CredMan
    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, SecretEnvelopeJsonContext.Default.SecretEnvelope);
    }

    public static SecretEnvelope? TryParse(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        // Backwards compat: bare base64 DPAPI blob (no leading {)
        string trimmed = stored.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return null;
        try
        {
            return JsonSerializer.Deserialize(stored, SecretEnvelopeJsonContext.Default.SecretEnvelope);
        }
        catch
        {
            return null;
        }
    }

    // Backwards compat: detect if a stored value is a bare DPAPI base64 blob
    // (pre-v0.8 format, no envelope wrapper)
    public static bool IsBareBase64Blob(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        string trimmed = stored.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == '{') return false;
        try
        {
            // Must be valid base64
            Convert.FromBase64String(stored);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
