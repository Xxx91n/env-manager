using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

// Ticket 08 (architecture-recovery): typed model of the env-manager-service IPC
// protocol. The authoritative schema lives in service/src/ipc.rs (Rust IpcRequest/
// IpcResponse); docs/schemas/env-manager-service-ipc.schema.json and ipc-samples.json
// are golden files exported from it. These C# mirrors and the xUnit contract tests
// in tests/EnvManager.Engine.Tests/ServiceIpcContractTests.cs must stay compatible
// with that schema — a renamed field on the Rust side fails the contract tests here.
// See docs/architecture.md "IPC Schema Contract (single source of truth)".

/// <summary>Request sent to the service over the named pipe (mirrors Rust IpcRequest).</summary>
class ServiceIpcRequest
{
    [JsonPropertyName("method")] public string Method { get; set; } = "";

    // Serialized as mount_id to match the Rust snake_case field.
    [JsonPropertyName("mount_id")] public string? MountId { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("request_id")] public string? RequestId { get; set; }

    public static string Serialize(ServiceIpcRequest request) =>
        JsonSerializer.Serialize(request, ServiceIpcJson.Options);

    /// <summary>
    /// Builds the request for a CLI `service &lt;subcommand&gt;` invocation.
    /// Unknown subcommands throw; the caller handles user-facing errors.
    /// </summary>
    public static ServiceIpcRequest FromSubcommand(string subcommand, string requestId, string? mountId = null) =>
        subcommand switch
        {
            "status" => new ServiceIpcRequest { Method = "status", RequestId = requestId },
            "health" => new ServiceIpcRequest { Method = "health", RequestId = requestId },
            "refresh" => new ServiceIpcRequest { Method = "refresh", MountId = mountId, RequestId = requestId },
            "rotate" => new ServiceIpcRequest { Method = "rotate", MountId = mountId, RequestId = requestId },
            "reload" => new ServiceIpcRequest { Method = "reload", RequestId = requestId },
            "shutdown" => new ServiceIpcRequest { Method = "shutdown", RequestId = requestId },
            "ping" => new ServiceIpcRequest { Method = "ping" },
            _ => throw new ArgumentException($"unknown service subcommand: {subcommand}")
        };
}

/// <summary>Response received from the service (mirrors Rust IpcResponse).</summary>
class ServiceIpcResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }

    [JsonPropertyName("data")] public JsonElement? Data { get; set; }

    [JsonPropertyName("message")] public string? Message { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }

    public static ServiceIpcResponse? Deserialize(string json) =>
        JsonSerializer.Deserialize<ServiceIpcResponse>(json, ServiceIpcJson.Options);

    /// <summary>
    /// Serializes a degraded (gateway-side) failure envelope with the extra
    /// `state` field the CLI reports when the service is unreachable.
    /// </summary>
    public static string SerializeDegraded(string state, string message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["state"] = state,
            ["message"] = message,
        };
        return JsonSerializer.Serialize(payload, ServiceIpcJson.Options);
    }
}

/// <summary>Shared serializer options for the IPC protocol (matches Rust serde skip_serializing_if = Option::is_none).</summary>
static class ServiceIpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
