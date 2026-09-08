// SecretProviderException.cs - typed provider error family (ticket 40, architecture-recovery)
// spec Phase 6 story 20: every ISecretProvider adapter boundary catches raw backend/transport
// exceptions, maps them onto this family, and every family message passes
// Program.ScrubExceptionMessage (ADR 0005) inside the base constructor - a secret-bearing
// fragment can never survive inside a family exception message. Defense against
// aws-sdk-java #2702 (Authorization header echoed into SDK exception messages) and
// azure-sdk-for-net #39594 (RequestFailedException message inconsistency).
// License: Apache-2.0

using System;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace EnvManager.Secrets.Core;

// Base class of the family. Derives from InvalidOperationException so every pre-existing
// catch/assert site keeps its behavior. Provider/Operation are always populated by the
// adapters; MountId carries the envelope target name where one was parsed (null otherwise -
// the boundary mapper does not re-parse possibly-garbage envelopes).
internal class SecretProviderException : InvalidOperationException
{
    public string Provider { get; }
    public string? MountId { get; }
    public string Operation { get; }

    protected SecretProviderException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(Program.ScrubExceptionMessage(message), inner)
    {
        Provider = provider;
        Operation = operation;
        MountId = mountId;
    }
}

internal sealed class SecretProviderAuthFailedException : SecretProviderException
{
    public SecretProviderAuthFailedException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(provider, operation, message, mountId, inner) { }
}

internal sealed class SecretProviderNotFoundException : SecretProviderException
{
    public SecretProviderNotFoundException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(provider, operation, message, mountId, inner) { }
}

internal sealed class SecretProviderPermissionDeniedException : SecretProviderException
{
    public SecretProviderPermissionDeniedException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(provider, operation, message, mountId, inner) { }
}

internal sealed class SecretProviderUnavailableException : SecretProviderException
{
    public SecretProviderUnavailableException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(provider, operation, message, mountId, inner) { }
}

internal sealed class SecretProviderTimeoutException : SecretProviderException
{
    public SecretProviderTimeoutException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(provider, operation, message, mountId, inner) { }
}

internal sealed class SecretProviderInvalidEnvelopeException : SecretProviderException
{
    public SecretProviderInvalidEnvelopeException(string provider, string operation, string message, string? mountId = null, Exception? inner = null)
        : base(provider, operation, message, mountId, inner) { }
}

// Classification + mapping helpers shared by the 8 provider adapters (ticket 40).
internal static class SecretProviderErrors
{
    public const string CatAuthFailed = "authFailed";
    public const string CatNotFound = "notFound";
    public const string CatPermissionDenied = "permissionDenied";
    public const string CatUnavailable = "unavailable";
    public const string CatTimeout = "timeout";
    public const string CatInvalidEnvelope = "invalidEnvelope";
    public const string CatUnknown = "unknown";

    // Adapter operation labels (SecretProviderException.Operation).
    public const string OpEncrypt = "encrypt";
    public const string OpDecrypt = "decrypt";
    public const string OpRotate = "rotate";
    public const string OpDelete = "delete";

    // Failure-kind bucket for SecretProviderManager.RotateAll classified counting and tests.
    public static string Classify(Exception ex) => ex switch
    {
        SecretProviderAuthFailedException => CatAuthFailed,
        SecretProviderNotFoundException => CatNotFound,
        SecretProviderPermissionDeniedException => CatPermissionDenied,
        SecretProviderTimeoutException => CatTimeout,
        SecretProviderInvalidEnvelopeException => CatInvalidEnvelope,
        SecretProviderException => CatUnavailable,
        HttpRequestException => CatUnavailable,
        TaskCanceledException or TimeoutException => CatTimeout,
        JsonException => CatInvalidEnvelope,
        Win32Exception => CatUnavailable,
        _ => CatUnknown
    };

    // Maps an HTTP status to a typed family exception. The message carries the status
    // classification only plus the optional detail (response body) VERBATIM - the family
    // base constructor runs the assembled message through ScrubExceptionMessage exactly
    // once (the scrubber is not idempotent, so never pre-scrub before calling this).
    // AWS passes detail: null - the adapter drops the response body entirely (issue 40).
    public static SecretProviderException FromStatus(string provider, string operation, HttpStatusCode status, string action, string? detail = null, string? mountId = null)
    {
        string body = string.IsNullOrEmpty(detail) ? "" : ": " + detail;
        string label = provider + " " + action + " failed (" + (int)status + " " + status + ")" + body;
        return (int)status switch
        {
            401 => new SecretProviderAuthFailedException(provider, operation, label, mountId),
            403 => new SecretProviderPermissionDeniedException(provider, operation, label, mountId),
            404 => new SecretProviderNotFoundException(provider, operation, label, mountId),
            _ => new SecretProviderUnavailableException(provider, operation, label, mountId)
        };
    }

    // Boundary mapper for foreign (non-family) exceptions escaping an adapter. The raw
    // message is dropped when discardRawMessage is set (AWS: issue 40 mandates
    // classification-only); otherwise the raw message rides along and is scrubbed once
    // by the family base constructor.
    public static SecretProviderException Map(string provider, string operation, Exception ex, string? mountId = null, bool discardRawMessage = false)
    {
        string detail = discardRawMessage ? "" : ": " + ex.Message;
        switch (ex)
        {
            case SecretProviderException already:
                return already;
            case Win32Exception w32:
                return MapWin32(provider, operation, w32, mountId);
            case HttpRequestException:
                return new SecretProviderUnavailableException(provider, operation,
                    provider + " " + operation + " failed: backend unreachable (network/TLS)" + detail, mountId, ex);
            case TaskCanceledException:
            case TimeoutException:
                return new SecretProviderTimeoutException(provider, operation,
                    provider + " " + operation + " timed out" + detail, mountId, ex);
            case JsonException:
                return new SecretProviderUnavailableException(provider, operation,
                    provider + " " + operation + " failed: malformed backend response" + detail, mountId, ex);
            default:
                return new SecretProviderUnavailableException(provider, operation,
                    provider + " " + operation + " failed (" + ex.GetType().Name + ")" + detail, mountId, ex);
        }
    }

    // Transport wrapper: runs one HTTP call (or response read/parse) and maps raw
    // transport failures (network, TLS, timeout, malformed payload) onto the family at
    // the adapter boundary. Family exceptions thrown by the wrapped call pass through
    // untouched (single-mapping guarantee). discardRawMessage drops the raw exception
    // message - AWS passes true (issue 40 audit: classification-only).
    public static T Send<T>(string provider, string operation, Func<T> call, string? mountId = null, bool discardRawMessage = false)
    {
        try { return call(); }
        catch (Exception ex) when (ex is not SecretProviderException)
        {
            throw Map(provider, operation, ex, mountId, discardRawMessage);
        }
    }

    // Best-effort cleanup boundary (provider-side Delete / temp-file removal / hash
    // recording): the failure must not propagate, and the message still passes the
    // scrubber so no secret-bearing fragment is retained or reported.
    public static void SwallowBestEffort(string provider, string operation, Exception ex)
    {
        _ = Program.ScrubExceptionMessage(provider + " " + operation + " cleanup failed: " + ex.Message);
    }

    // Boundary mapping that PRESERVES the original message text (scrubbed once by the
    // family base constructor). Used where a pinned CLI snapshot or static BCL message
    // must survive verbatim - only the exception type changes (snapshot-safe mapping).
    public static SecretProviderException MapPreserveMessage(string provider, string operation, Exception ex, string? mountId = null)
    {
        if (ex is SecretProviderException already) return already;
        return new SecretProviderUnavailableException(provider, operation, ex.Message, mountId, ex);
    }

    // Message-preserving Win32 mapping: the pinned-style message stays verbatim
    // (scrubbed once by the family base constructor) while the classification derives
    // from the native error code - CredentialManager CredReadW/CredWriteW path.
    public static SecretProviderException MappedWin32(string provider, string operation, string message, Win32Exception w32, string? mountId = null)
    {
        int code = w32.NativeErrorCode;
        return (code == 1168 || code == 1163)
            ? new SecretProviderNotFoundException(provider, operation, message, mountId, w32)
            : (code == 5 || code == 1008 || code == 1314)
                ? new SecretProviderPermissionDeniedException(provider, operation, message, mountId, w32)
                : new SecretProviderUnavailableException(provider, operation, message, mountId, w32);
    }

    private static SecretProviderException MapWin32(string provider, string operation, Win32Exception w32, string? mountId)
    {
        int code = w32.NativeErrorCode;
        string label = " (Win32 error " + code + ")";
        if (code == 5 || code == 1008 || code == 1314) // ERROR_ACCESS_DENIED / NO_TOKEN / PRIVILEGE_NOT_HELD
            return new SecretProviderPermissionDeniedException(provider, operation, provider + " " + operation + " denied" + label, mountId, w32);
        if (code == 1168 || code == 1163) // ERROR_NOT_FOUND / ERROR_NO_MORE_ITEMS
            return new SecretProviderNotFoundException(provider, operation, provider + " " + operation + " target not found" + label, mountId, w32);
        return new SecretProviderUnavailableException(provider, operation, provider + " " + operation + " failed" + label, mountId, w32);
    }
}
