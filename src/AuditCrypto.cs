// v0.9.13 Phase 3B/3C: Audit at-rest encryption + Export-state double-layer crypto.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EnvManager;

partial class Program
{
    private const int AuditNonceSize = 12;
    private const int AuditTagSize = 16;
    private const int AuditKeySize = 32;

    private static string? _auditKeyPathForTests;

    internal static void SetAuditKeyPathForTests(string? path) => _auditKeyPathForTests = path;

    private static string AuditKeyPath => _auditKeyPathForTests ?? Path.Combine(
        LocalAppDataRoot,
        "EnvManager", "audit.key");

    /// <summary>
    /// Get or create the AES-256 DEK, DPAPI-CurrentUser-encrypted on disk.
    /// </summary>
    private static byte[] GetOrCreateAuditDek()
    {
        try
        {
            if (File.Exists(AuditKeyPath))
            {
                var keyBase64 = File.ReadAllText(AuditKeyPath).Trim();
                return Convert.FromBase64String(DpapiHelper.DecryptSecret(keyBase64));
            }
        }
        catch (Exception ex)
        {
            DebugLog("AuditCrypto: failed to read/decrypt DEK: " + ex.GetType().Name);
        }

        var newKey = new byte[AuditKeySize];
        RandomNumberGenerator.Fill(newKey);
        try
        {
            var encrypted = DpapiHelper.EncryptSecret(Convert.ToBase64String(newKey));
            WriteAtomicUtf8(AuditKeyPath, encrypted);
            SetFileAclRestricted(AuditKeyPath);
        }
        catch (Exception ex)
        {
            DebugLog("AuditCrypto: failed to persist DEK: " + ex.GetType().Name);
        }
        return newKey;
    }

    /// <summary>
    /// Encrypt a UTF-8 string with AES-256-GCM. Returns base64 of nonce|ciphertext|tag.
    /// </summary>
    private static string EncryptAuditGcm(string plaintext, byte[] key)
    {
        var ptBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[AuditNonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[ptBytes.Length];
        var tag = new byte[AuditTagSize];

        using var aes = new AesGcm(key, AuditTagSize);
        aes.Encrypt(nonce, ptBytes, ciphertext, tag);

        var envelope = new byte[AuditNonceSize + ciphertext.Length + AuditTagSize];
        Buffer.BlockCopy(nonce, 0, envelope, 0, AuditNonceSize);
        Buffer.BlockCopy(ciphertext, 0, envelope, AuditNonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, envelope, AuditNonceSize + ciphertext.Length, AuditTagSize);

        return Convert.ToBase64String(envelope);
    }

    /// <summary>
    /// Decrypt a base64 envelope with AES-256-GCM.
    /// </summary>
    private static string DecryptAuditGcm(string base64Envelope, byte[] key)
    {
        var envelope = Convert.FromBase64String(base64Envelope);
        if (envelope.Length < AuditNonceSize + AuditTagSize)
            throw new ArgumentException("Invalid envelope length");

        int ctLen = envelope.Length - AuditNonceSize - AuditTagSize;
        var nonce = new byte[AuditNonceSize];
        var ct = new byte[ctLen];
        var tag = new byte[AuditTagSize];

        Buffer.BlockCopy(envelope, 0, nonce, 0, AuditNonceSize);
        Buffer.BlockCopy(envelope, AuditNonceSize, ct, 0, ctLen);
        Buffer.BlockCopy(envelope, AuditNonceSize + ctLen, tag, 0, AuditTagSize);

        var pt = new byte[ctLen];
        using var aes = new AesGcm(key, AuditTagSize);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>
    /// Check if content looks like an encrypted envelope (not plain JSON).
    /// </summary>
    private static bool IsEncryptedEnvelope(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;
        string t = content.TrimStart();
        if (t.StartsWith('[') || t.StartsWith('{')) return false;
        try
        {
            var decoded = Convert.FromBase64String(t);
            return decoded.Length >= AuditNonceSize + AuditTagSize;
        }
        catch { return false; }
    }

    /// <summary>
    /// Encrypt audit content. Fail-open: on error, returns plaintext.
    /// </summary>
    private static string EncryptAuditContent(string plaintext)
    {
        try
        {
            var key = GetOrCreateAuditDek();
            return EncryptAuditGcm(plaintext, key);
        }
        catch (Exception ex)
        {
            DebugLog("AuditCrypto: encrypt failed, writing plaintext: " + ex.GetType().Name);
            return plaintext;
        }
    }

    /// <summary>
    /// Decrypt audit content. Auto-detects encrypted vs plain JSON.
    /// </summary>
    private static string DecryptAuditContent(string content)
    {
        if (!IsEncryptedEnvelope(content)) return content;
        try
        {
            var key = GetOrCreateAuditDek();
            return DecryptAuditGcm(content, key);
        }
        catch (Exception ex)
        {
            DebugLog("AuditCrypto: decrypt failed, returning raw: " + ex.GetType().Name);
            return content;
        }
    }

    // === Phase 3C: Export-state double-layer crypto ===

    /// <summary>
    /// Export: AES-256-GCM encrypt payload, DPAPI encrypt DEK, HMAC-SHA256 for integrity.
    /// </summary>
    private static string ExportStateEncrypt(string plaintext)
    {
        var dek = new byte[AuditKeySize];
        RandomNumberGenerator.Fill(dek);

        var ptBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[AuditNonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ct = new byte[ptBytes.Length];
        var tag = new byte[AuditTagSize];

        using var aes = new AesGcm(dek, AuditTagSize);
        aes.Encrypt(nonce, ptBytes, ct, tag);

        using var hmac = new HMACSHA256(dek);
        var hmacBytes = hmac.ComputeHash(ct);

        var dpapiDek = DpapiHelper.EncryptSecret(Convert.ToBase64String(dek));

        return JsonSerializer.Serialize(new
        {
            version = 2,
            hmac = Convert.ToHexString(hmacBytes),
            nonce = Convert.ToBase64String(nonce),
            ciphertext = Convert.ToBase64String(ct),
            tag = Convert.ToBase64String(tag),
            dpapi_dek = dpapiDek
        });
    }

    /// <summary>
    /// Import: validate HMAC, DPAPI decrypt DEK, AES-GCM decrypt payload.
    /// </summary>
    private static string ExportStateDecrypt(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        var root = doc.RootElement;

        int version = root.GetProperty("version").GetInt32();
        if (version == 1)
        {
            return DpapiHelper.DecryptSecret(envelopeJson.Trim('"'));
        }
        if (version != 2)
            throw new FormatException($"Unsupported export version: {version}");

        string hmacHex = root.GetProperty("hmac").GetString()!;
        string nonceB64 = root.GetProperty("nonce").GetString()!;
        string ctB64 = root.GetProperty("ciphertext").GetString()!;
        string tagB64 = root.GetProperty("tag").GetString()!;
        string dpapiDekB64 = root.GetProperty("dpapi_dek").GetString()!;

        var dek = Convert.FromBase64String(DpapiHelper.DecryptSecret(dpapiDekB64));
        var ct = Convert.FromBase64String(ctB64);

        using var hmac = new HMACSHA256(dek);
        var computed = hmac.ComputeHash(ct);
        var expected = Convert.FromHexString(hmacHex);
        if (!CryptographicOperations.FixedTimeEquals(computed, expected))
            throw new CryptographicException("HMAC verification failed — export file may have been tampered with");

        var nonce = Convert.FromBase64String(nonceB64);
        var tag = Convert.FromBase64String(tagB64);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(dek, AuditTagSize);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    /// <summary>
    /// Detect if export file is v1 (pure DPAPI) or v2 (double-layer).
    /// </summary>
    private static int ExportStateDetectVersion(string content)
    {
        string t = content.Trim();
        if (t.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                if (doc.RootElement.TryGetProperty("version", out var v))
                    return v.GetInt32();
            }
            catch { }
        }
        return 1;
    }
}
