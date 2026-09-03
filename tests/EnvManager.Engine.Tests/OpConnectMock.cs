// OpConnectMock.cs - minimal 1Password Connect REST stub for the L1 matrix (issue 15)
// License: Apache-2.0
//
// OnePasswordProvider shells out to the real op CLI. There is no offline 1Password
// backend (research: no official test double; the community pattern is a Connect
// mock), so the L1 harness runs the REAL op binary against this in-process Connect
// REST stub on localhost - the provider's full path (binary discovery, argument
// marshaling, JSON envelope handling) executes unchanged; only the cloud service is
// replaced by a localhost double, exactly like the Testcontainers emulators do for
// the HTTP providers. The stub implements the three endpoints the provider's flows
// touch (create item, get item by id, delete item) plus the vault list op uses to
// resolve OP_VAULT by name (Connect REST spec v1.8.1 subset).

using System.Net;
using System.Text;
using System.Text.Json;

namespace EnvManager.Engine.Tests;

internal sealed class OpConnectMock : IDisposable
{
    private const string VaultId = "01980000-0000-7000-8000-000000000001";
    private const string VaultName = "EnvManager";

    private readonly HttpListener _listener;
    private readonly List<JsonElement> _items = [];
    private readonly object _lock = new();
    private int _nextItemId;

    // vault name op last resolved by (name-form first, then id-form; items echo must match)
    private string _lastVaultTitle = VaultName;


    private OpConnectMock(HttpListener listener, string token, int port)
    {
        _listener = listener;
        Token = token;
        Port = port;
    }

    internal string Token { get; }

    internal int Port { get; }

    internal string ConnectHost => $"http://127.0.0.1:{Port}";

    public void Dispose() => ((IDisposable)_listener).Dispose();

    internal static OpConnectMock Start()
    {
        var token = "em-l1-connect-token-" + Guid.NewGuid().ToString("N");
        var listener = new HttpListener();
        // port 0 is not supported by HttpListener; grab a free ephemeral port first
        var port = GetFreeEphemeralPort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var mock = new OpConnectMock(listener, token, port);
        var worker = new Thread(mock.ServeLoop) { IsBackground = true, Name = "op-connect-mock" };
        worker.Start();
        return mock;
    }

    private static int GetFreeEphemeralPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private void ServeLoop()
    {
        // The loop must survive handler exceptions: a dead reader leaves the listener
        // accepting connections that are never answered (client-side hang).
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = _listener.GetContext();
            }
            catch (Exception)
            {
                return; // disposed
            }

            try
            {
                LogRequest(ctx);
                Handle(ctx);
            }
            catch (Exception ex)
            {
                LogLine("HANDLER ERROR " + ex.GetType().Name + ": " + ex.Message);
                try { WriteJson(ctx.Response, 500, JsonSerializer.Serialize(new { error = ex.Message })); } catch { }
            }
        }
    }

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "env-manager-l1-opmock.log");

    private static void LogRequest(HttpListenerContext ctx) =>
        LogLine(ctx.Request.HttpMethod + " " + ctx.Request.Url?.PathAndQuery);

    private static void LogLine(string line)
    {
        try
        {
            File.AppendAllText(LogPath, DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
        }
        catch { }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        System.Diagnostics.Debug.WriteLine("[op-connect-mock] " + req.HttpMethod + " " + req.Url?.PathAndQuery);

        if (req.Headers["Authorization"] != "Bearer " + Token)
        {
            WriteJson(res, 401, "{}");
            return;
        }

        var path = req.Url!.AbsolutePath.TrimEnd('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // GET /v1/vaults -> vault list (op resolves OP_VAULT by name)
        if (req.HttpMethod == "GET" && segments.Length == 2 && segments[0] == "v1" && segments[1] == "vaults")
        {
            // op resolves --vault by matching the returned vault's NAME against the
            // requested title: op issues BOTH name-form and id-form vault queries and
            // each must echo the requested value as the vault name, otherwise the
            // resolution never converges (verified against op 2.39.0; 2 vault queries
            // + 1 items query per resolution round).
            var requestedTitle = VaultName;
            var vaultBody = JsonSerializer.Serialize(new[]
            {
                new { id = VaultId, name = requestedTitle, attributeVersion = 1, contentVersion = 1, items = _items.Count, type = "USER_CREATED" },
            });
            _lastVaultTitle = requestedTitle;
            LogLine("VAULT-RESP[" + vaultBody + "]");
            WriteJson(res, 200, vaultBody);
            return;
        }

        // GET /v1/vaults/{id}/items -> item list. op resolves item get <id> by listing
        // with filter=title eq "<id or title>" and matching server-side; a list response
        // that ignores the filter makes op retry forever. Echo the requested title onto
        // the matching item (by id or title) so resolution converges.
        if (req.HttpMethod == "GET" && segments.Length == 4 && segments[1] == "vaults" && segments[2] == VaultId && segments[3] == "items")
        {
            lock (_lock)
            {
                var filterTitle = ParseTitleFilter(req.Url!.Query);
                if (filterTitle is null)
                {
                    WriteJson(res, 200, JsonSerializer.Serialize(_items));
                    return;
                }
                var matches = _items.Where(i =>
                    (i.TryGetProperty("id", out var pid) && pid.GetString() == filterTitle) ||
                    (i.TryGetProperty("title", out var ptitle) && ptitle.GetString() == filterTitle)).ToList();
                var echoed = matches.Select(i =>
                {
                    var buf = JsonSerializer.Serialize(new
                    {
                        id = i.TryGetProperty("id", out var pid2) ? pid2.GetString() : "",
                        title = filterTitle,
                        vault = new { id = VaultId, name = _lastVaultTitle },
                        category = i.TryGetProperty("category", out var pc) ? pc.GetString() : "PASSWORD",
                        fields = i.TryGetProperty("fields", out var pf) ? pf : JsonSerializer.SerializeToElement(Array.Empty<object>()),
                        createdAt = i.TryGetProperty("createdAt", out var pc1) ? pc1.GetString() : "",
                        updatedAt = i.TryGetProperty("updatedAt", out var pc2) ? pc2.GetString() : "",
                        version = 1,
                        
                        lastEditedBy = "01980000-0000-7000-8000-0000000000fe",
                    });
                    using var d = JsonDocument.Parse(buf);
                    return d.RootElement.Clone();
                }).ToList();
                var bodyOut = JsonSerializer.Serialize(echoed);
                LogLine("LIST-RESP[" + bodyOut + "]");
                WriteJson(res, 200, bodyOut);
                return;
            }
        }

        // POST /v1/vaults/{id}/items -> create
        if (req.HttpMethod == "POST" && segments.Length == 4 && segments[1] == "vaults" && segments[2] == VaultId && segments[3] == "items")
        {
            using var doc = JsonDocument.Parse(ReadBody(req));
            var root = doc.RootElement.Clone();
            lock (_lock)
            {
                var id = $"01980000-0000-7000-8000-{(_nextItemId++).ToString("D12")}";
                var created = JsonSerializer.Serialize(new
                {
                    id,
                    title = root.TryGetProperty("title", out var t) ? t.GetString() : "",
                    vault = new { id = VaultId, name = VaultName },
                    category = root.TryGetProperty("category", out var c) ? c.GetString() : "PASSWORD",
                    fields = root.TryGetProperty("fields", out var f) ? f : (JsonElement)JsonSerializer.SerializeToElement(Array.Empty<object>()),
                    // op's Go time.Time expects RFC3339 UTC "Z" form; ".o" emits "+00:00"
                    createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    updatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    version = 1,
                    
                    lastEditedBy = "01980000-0000-7000-8000-0000000000fe",
                });
                _items.Add(JsonDocument.Parse(created).RootElement.Clone());
                WriteJson(res, 200, created);
                return;
            }
        }

        // GET/DELETE /v1/vaults/{id}/items/{itemId}
        if (segments.Length == 5 && segments[1] == "vaults" && segments[2] == VaultId && segments[3] == "items")
        {
            var itemId = segments[4];
            lock (_lock)
            {
                var idx = _items.FindIndex(i => i.TryGetProperty("id", out var id) && id.GetString() == itemId);
                if (req.HttpMethod == "GET")
                {
                    if (idx < 0) { WriteJson(res, 404, "{}"); return; }
                    WriteJson(res, 200, _items[idx].GetRawText());
                    return;
                }
                if (req.HttpMethod == "DELETE")
                {
                    if (idx >= 0) _items.RemoveAt(idx);
                    res.StatusCode = 204;
                    res.Close();
                    return;
                }
            }
        }

        WriteJson(res, 404, "{}");
    }

    private static string? ParseTitleFilter(string query)
    {
        // op sends filter=title+eq+%22X%22: '+' is the space form; UnescapeDataString
        // does NOT decode '+', so normalize it first (live-verified against op 2.39.0 -
        // missing this made every list response look filterless and op retried forever).
        var decoded = Uri.UnescapeDataString(query).Replace('+', ' ');
        var idx = decoded.IndexOf("title eq \"", StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + "title eq \"".Length;
        var end = decoded.IndexOf('"', start);
        return end > start ? decoded[start..end] : null;
    }

    private static string ReadBody(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteJson(HttpListenerResponse res, int status, string body)
    {
        res.StatusCode = status;
        res.ContentType = "application/json";
        // Real Connect servers stamp every response with their API version; the
        // SDK-family clients treat a missing header as "Connect <=1.2" and op's
        // resolver never converges without it (found via connect-sdk-go's
        // VersionHeaderKey contract).
        res.Headers.Add("1Password-Connect-Version", "1.8.1");
        var bytes = Encoding.UTF8.GetBytes(body);
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }
}
