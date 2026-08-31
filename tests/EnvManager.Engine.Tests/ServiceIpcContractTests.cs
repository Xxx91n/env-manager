using System.Text.Json;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: IPC schema contract (architecture-recovery issue 08).
/// The authoritative protocol definition lives in the Rust service
/// (service/src/ipc.rs IpcRequest/IpcResponse). docs/schemas/ holds golden
/// files exported from it by the Rust golden test. These tests assert the
/// C# gateway (ServiceIpc.cs) stays wire-compatible with that schema:
/// every golden sample round-trips through the C# types, and every request
/// the CLI gateway emits deserializes against the exported JSON Schema's
/// required fields and property names.
/// </summary>
public class ServiceIpcContractTests
{
    static string FindRepoRoot()
    {
        // Walk up from the test assembly location to the directory containing docs/schemas.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "schemas");
            if (Directory.Exists(candidate)) return dir.FullName;
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("docs/schemas/ not found above test assembly");
    }

    static JsonDocument LoadSamples()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "schemas", "ipc-samples.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    public static TheoryData<string, JsonElement> RequestSamples()
    {
        var data = new TheoryData<string, JsonElement>();
        using var doc = LoadSamples();
        foreach (var s in doc.RootElement.GetProperty("requests").EnumerateArray())
            data.Add(s.GetProperty("name").GetString()!, s.GetProperty("payload").Clone());
        return data;
    }

    public static TheoryData<string, JsonElement> ResponseSamples()
    {
        var data = new TheoryData<string, JsonElement>();
        using var doc = LoadSamples();
        foreach (var s in doc.RootElement.GetProperty("responses").EnumerateArray())
            data.Add(s.GetProperty("name").GetString()!, s.GetProperty("payload").Clone());
        return data;
    }

    [Theory]
    [MemberData(nameof(RequestSamples))]
    public void RequestSamples_DeserializeIntoCsharpContract(string name, JsonElement payload)
    {
        var req = payload.Deserialize<ServiceIpcRequest>(ServiceIpcJson.Options);
        Assert.NotNull(req);
        Assert.False(string.IsNullOrEmpty(req!.Method), $"sample {name} lost its method");
    }

    [Theory]
    [MemberData(nameof(ResponseSamples))]
    public void ResponseSamples_DeserializeIntoCsharpContract(string name, JsonElement payload)
    {
        var resp = payload.Deserialize<ServiceIpcResponse>(ServiceIpcJson.Options);
        Assert.NotNull(resp);
        Assert.Equal(payload.GetProperty("ok").GetBoolean(), resp!.Ok);
        if (resp.Ok)
            Assert.True(resp.Data is not null, $"sample {name} should carry data");
    }

    /// <summary>
    /// The CLI gateway's serialization must produce exactly the field names the
    /// Rust side expects (method / mount_id / request_id). A rename of the C#
    /// JsonPropertyName attribute or the Rust field drifts this red.
    /// </summary>
    [Theory]
    [InlineData("status", "{\"method\":\"status\",\"request_id\":\"r1\"}")]
    [InlineData("health", "{\"method\":\"health\",\"request_id\":\"r1\"}")]
    [InlineData("ping", "{\"method\":\"ping\"}")]
    [InlineData("reload", "{\"method\":\"reload\",\"request_id\":\"r1\"}")]
    [InlineData("shutdown", "{\"method\":\"shutdown\",\"request_id\":\"r1\"}")]
    public void GatewayRequestWireFormat_MatchesRustSchema(string subcommand, string expectedJson)
    {
        var req = ServiceIpcRequest.FromSubcommand(subcommand, "r1");
        Assert.Equal(expectedJson, ServiceIpcRequest.Serialize(req));
    }

    [Fact]
    public void GatewayMountCarriesRequest_SnakeCaseWireNames()
    {
        var req = ServiceIpcRequest.FromSubcommand("refresh", "r1", "vault-team");
        var json = ServiceIpcRequest.Serialize(req);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("refresh", root.GetProperty("method").GetString());
        Assert.Equal("vault-team", root.GetProperty("mount_id").GetString());
        Assert.Equal("r1", root.GetProperty("request_id").GetString());
        Assert.False(root.TryGetProperty("mountId", out _), "camelCase mountId must not appear on the wire");
    }

    /// <summary>
    /// Degraded gateway responses (service unreachable) must keep the documented
    /// envelope: ok=false plus `state`, consumed by the TS parseServiceResponse.
    /// </summary>
    [Fact]
    public void DegradedEnvelope_RoundTripsThroughContract()
    {
        var json = ServiceIpcResponse.SerializeDegraded("not_running", "service not_running");
        var parsed = ServiceIpcResponse.Deserialize(json);
        Assert.NotNull(parsed);
        Assert.False(parsed!.Ok);
        Assert.Equal("service not_running", parsed.Message);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("not_running", doc.RootElement.GetProperty("state").GetString());
    }

    /// <summary>
    /// Cross-checks the C# type surface against the exported JSON Schema: every
    /// property the schema declares on IpcRequest/IpcResponse must exist on the
    /// C# contract with the same wire name. Catches schema-side renames too.
    /// </summary>
    [Fact]
    public void CsharpContract_CoversSchemaPropertyNames()
    {
        var schemaPath = Path.Combine(FindRepoRoot(), "docs", "schemas", "env-manager-service-ipc.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        var requestProps = schema.RootElement.GetProperty("IpcRequest").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToHashSet();
        var responseProps = schema.RootElement.GetProperty("IpcResponse").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToHashSet();

        Assert.Equal(new HashSet<string> { "method", "mount_id", "id", "request_id" }, requestProps);
        Assert.Equal(new HashSet<string> { "ok", "data", "message", "id" }, responseProps);

        // Wire-name coverage on the C# side: serialize a fully-populated instance
        // and confirm every schema property name appears.
        var reqJson = JsonSerializer.Serialize(
            new ServiceIpcRequest { Method = "status", MountId = "m", Id = "i", RequestId = "r" },
            ServiceIpcJson.Options);
        using var reqDoc = JsonDocument.Parse(reqJson);
        var reqNames = reqDoc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Subset(reqNames, requestProps);
        Assert.Equal(requestProps, reqNames);

        var respJson = JsonSerializer.Serialize(
            new ServiceIpcResponse { Ok = true, Data = JsonDocument.Parse("true").RootElement, Message = "m", Id = "i" },
            ServiceIpcJson.Options);
        using var respDoc = JsonDocument.Parse(respJson);
        var respNames = respDoc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(responseProps, respNames);
    }
}
