// ReadOnlyProbeWireTests.cs - the four cloud providers' availability probe sends reads only
// License: Apache-2.0

// Issue #277. `profile secret-provider list` probes every network provider, and the
// profile editor calls `list` on mount. The four providers below keep their data in a
// customer account (Vault, Azure Key Vault, AWS Secrets Manager, 1Password), so the
// probe has to be provably read-only: the old sentinel round-trip created a secret and
// then deleted it on every page load.
//
// Each test installs a fake transport, drives `ProbeReadOnly` through the responses a
// real backend gives (404-style "not found" is the success signal), and asserts:
//   - exactly the read-shaped requests were sent (no PUT/POST/DELETE against the store),
//   - the probe path cannot collide with a real secret,
//   - a permission/auth answer becomes the reason code the CLI reports.
// No network access: the fake handler answers in-process.

using System.Net;
using System.Net.Http;

using Xunit;
using EnvManager.Secrets.Core;
using EnvManager.Secrets.Providers;

namespace EnvManager.Engine.Tests;

public sealed class ReadOnlyProbeWireTests
{
    /// <summary>Answers every request with one canned response and records what arrived.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string bodyText = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, bodyText));
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            };
        }
    }

    /// <summary>Answers by request path, so a probe can meet a success then a denial.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { _respond = respond; }

        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string bodyText = request.Content == null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, bodyText));
            return _respond(request);
        }
    }

    /// <summary>Wraps an AWS sender so the signed request can be inspected before it travels.</summary>
    private sealed class RecordingSender : IAwsHttpSender
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingSender(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public List<(string Method, string Target, string Body)> Requests { get; } = new();

        public HttpResponseMessage Send(HttpRequestMessage request, TimeSpan timeout)
        {
            string bodyText = request.Content == null
                ? ""
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            request.Headers.TryGetValues("X-Amz-Target", out var targets);
            Requests.Add((request.Method.Method, targets?.FirstOrDefault() ?? "", bodyText));
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }

    private static void WithEnv(string name, string? value, Action body)
    {
        string? saved = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, saved);
        }
    }

    // ---------------- Vault ----------------

    [Fact]
    public void VaultProbe_ReadsANonexistentPath_AndNeverWrites()
    {
        // 404 + an empty error list is Vault's "this path does not exist, and you may
        // read here" answer; that is the capability a real round-trip needs.
        var handler = new StubHandler(HttpStatusCode.NotFound, "{\"errors\":[]}");
        var provider = new VaultKV2Provider(handler);

        WithEnv("VAULT_ADDR", "https://127.0.0.1:8200", () =>
            WithEnv("VAULT_TOKEN", "probe-token", () =>
                provider.ProbeReadOnly()));

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        // The probe path must come from the same mount and prefix Encrypt writes into,
        // and must never name a secret a user could have created.
        Assert.StartsWith("/v1/secret/data/env-manager/__env_manager_probe__", request.Path);
        Assert.Empty(request.Body);
    }

    [Fact]
    public void VaultProbe_MapsA403ToThePermissionReason()
    {
        // Vault answers 403 for a dead token and for a missing read policy alike, on
        // purpose; the reason code has to cover both rather than guess.
        var handler = new StubHandler(HttpStatusCode.Forbidden, "{\"errors\":[\"permission denied\"]}");
        var provider = new VaultKV2Provider(handler);

        Exception? thrown = null;
        WithEnv("VAULT_ADDR", "https://127.0.0.1:8200", () =>
            WithEnv("VAULT_TOKEN", "probe-token", () =>
            {
                try { provider.ProbeReadOnly(); }
                catch (Exception ex) { thrown = ex; }
            }));

        Assert.NotNull(thrown);
        Assert.Equal(SecretProviderErrors.ReasonPermission, SecretProviderErrors.ReasonCode(thrown!));
        Assert.Equal(SecretProviderErrors.OpProbe, ((SecretProviderException)thrown!).Operation);
    }

    [Fact]
    public void VaultProbe_TreatsAMissingMountAsUnavailable()
    {
        // A 404 that names a route failure means there is no secret mount where Encrypt
        // writes. Reporting "available" there would let the user pick a provider that
        // cannot store anything.
        var handler = new StubHandler(HttpStatusCode.NotFound,
            "{\"errors\":[\"no handler for route 'secret/data/env-manager/__env_manager_probe__x'\"]}");
        var provider = new VaultKV2Provider(handler);

        Exception? thrown = null;
        WithEnv("VAULT_ADDR", "https://127.0.0.1:8200", () =>
            WithEnv("VAULT_TOKEN", "probe-token", () =>
            {
                try { provider.ProbeReadOnly(); }
                catch (Exception ex) { thrown = ex; }
            }));

        Assert.NotNull(thrown);
        Assert.Equal(SecretProviderErrors.ReasonNetwork, SecretProviderErrors.ReasonCode(thrown!));
    }

    [Fact]
    public void VaultProbe_RefusesA404ThatIsNotVaultsEmptyErrorList()
    {
        // A proxy, a WAF or a truncated reply can answer 404 with a body Vault never
        // sends. Treating every 404 as "the token may read here" would report a provider
        // as usable when the thing at VAULT_ADDR is not answering for it at all.
        var handler = new StubHandler(HttpStatusCode.NotFound, "<html>Not Found</html>");
        var provider = new VaultKV2Provider(handler);

        Exception? thrown = null;
        WithEnv("VAULT_ADDR", "https://127.0.0.1:8200", () =>
            WithEnv("VAULT_TOKEN", "probe-token", () =>
            {
                try { provider.ProbeReadOnly(); }
                catch (Exception ex) { thrown = ex; }
            }));

        Assert.NotNull(thrown);
        Assert.Equal(SecretProviderErrors.ReasonNetwork, SecretProviderErrors.ReasonCode(thrown!));
    }

    [Fact]
    public void VaultProbe_RefusesA404WithANonEmptyErrorList()
    {
        // The other half of the same rule: `{"errors":["..."]}` is not the empty list,
        // so it is not Vault saying "the path is absent".
        var handler = new StubHandler(HttpStatusCode.NotFound, "{\"errors\":[\"something went wrong\"]}");
        var provider = new VaultKV2Provider(handler);

        Exception? thrown = null;
        WithEnv("VAULT_ADDR", "https://127.0.0.1:8200", () =>
            WithEnv("VAULT_TOKEN", "probe-token", () =>
            {
                try { provider.ProbeReadOnly(); }
                catch (Exception ex) { thrown = ex; }
            }));

        Assert.NotNull(thrown);
    }

    // ---------------- Azure Key Vault ----------------

    [Fact]
    public void AzureProbe_ReadsANonexistentSecret_AndNeverWrites()
    {
        // The AAD token exchange is out of scope here: the probe is driven through the
        // managed-identity seam, which answers in-process through the same handler.
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/secrets/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"SecretNotFound\",\"message\":\"Secret not found\"}}")
                };
            }
            // The identity endpoint answers a token; the vault URL only accepts bearer.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"probe-token\",\"expires_on\":\"9999999999\"}")
            };
        });
        var provider = new AzureKeyVaultProvider(handler);

        WithEnv("AZURE_KEYVAULT_URI", "https://probe.vault.azure.net", () =>
            WithEnv("IDENTITY_ENDPOINT", "https://probe.vault.azure.net/identity/token", () =>
                provider.ProbeReadOnly()));

        // The secret read is the only request that may touch the vault's data plane.
        var dataPlane = handler.Requests.Where(r => r.Path.Contains("/secrets/", StringComparison.Ordinal)).ToList();
        Assert.Single(dataPlane);
        Assert.Equal(HttpMethod.Get, dataPlane[0].Method);
        Assert.StartsWith("/secrets/env-manager-probe-", dataPlane[0].Path);
        Assert.Empty(dataPlane[0].Body);
        Assert.DoesNotContain(handler.Requests, r =>
            r.Method == HttpMethod.Put || r.Method == HttpMethod.Delete || r.Method == HttpMethod.Post && r.Path.Contains("/secrets/", StringComparison.Ordinal));
    }

    [Fact]
    public void AzureProbe_MapsA403ToThePermissionReason()
    {
        // Key Vault Reader reads metadata and still gets 403 on a value; that must not
        // be mistaken for "the vault is fine".
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/secrets/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":{\"code\":\"Forbidden\"}}") };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"probe-token\",\"expires_on\":\"9999999999\"}")
            };
        });
        var provider = new AzureKeyVaultProvider(handler);

        Exception? thrown = null;
        WithEnv("AZURE_KEYVAULT_URI", "https://probe.vault.azure.net", () =>
            WithEnv("IDENTITY_ENDPOINT", "https://probe.vault.azure.net/identity/token", () =>
            {
                try { provider.ProbeReadOnly(); }
                catch (Exception ex) { thrown = ex; }
            }));

        Assert.NotNull(thrown);
        Assert.Equal(SecretProviderErrors.ReasonPermission, SecretProviderErrors.ReasonCode(thrown!));
    }

    [Fact]
    public void AzureBudgets_NormalReadsKeepTenSeconds_TheProbeKeepsFive()
    {
        // A probe budget must not leak into a normal Encrypt/Decrypt. This pins the two
        // values directly: shortening the shared helper for the probe once cut the
        // managed-identity attempt on real reads to half its budget, so an endpoint
        // answering between 5s and 10s failed where it used to succeed. A test that slept
        // past the budget could not catch it - the fake handler ignores cancellation.
        Assert.Equal(TimeSpan.FromSeconds(10), AzureKeyVaultProvider.NormalTokenTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), AzureKeyVaultProvider.ProbeTimeoutBudget);
        Assert.NotEqual(AzureKeyVaultProvider.NormalTokenTimeout, AzureKeyVaultProvider.ProbeTimeoutBudget);
    }

    // ---------------- AWS Secrets Manager ----------------

    [Fact]
    public void AwsProbe_SendsAGetSecretValueAndNeverChanges()
    {
        // AWS answers "not found" as HTTP 400 with ResourceNotFoundException, not 404,
        // so the body decides. GetSecretValue is the cheapest probe: it needs exactly
        // the permission a real read needs, and no ListSecrets grant.
        var sender = new RecordingSender(HttpStatusCode.BadRequest,
            "{\"__type\":\"ResourceNotFoundException\",\"Message\":\"Secrets Manager can't find the requested secret\"}");
        var provider = new AwsSecretsManagerProvider(sender);

        WithEnv("AWS_REGION", "us-east-1", () =>
            WithEnv("AWS_ACCESS_KEY_ID", "probe-key", () =>
                WithEnv("AWS_SECRET_ACCESS_KEY", "probe-secret", () =>
                    provider.ProbeReadOnly())));

        Assert.Single(sender.Requests);
        Assert.Equal("secretsmanager.GetSecretValue", sender.Requests[0].Target);
        Assert.StartsWith("{\"SecretId\":\"env-manager-probe-", sender.Requests[0].Body);
        // No CreateSecret / DeleteSecret / PutSecretValue target may appear.
        Assert.DoesNotContain("CreateSecret", sender.Requests[0].Target);
        Assert.DoesNotContain("DeleteSecret", sender.Requests[0].Target);
        Assert.DoesNotContain("PutSecretValue", sender.Requests[0].Target);
    }

    [Fact]
    public void AwsProbe_ReportsARefusalAsUnknown_BecauseOneTargetCannotTellTheTwoCausesApart()
    {
        // A policy scoped to specific secret ARNs refuses this probe name even though real
        // reads work. Calling that "permission denied" would mark a working provider
        // unavailable, so the code is `unknown` - the reading is spelled out in the
        // message instead, and `set` still refuses to activate either way.
        var sender = new RecordingSender(HttpStatusCode.Forbidden,
            "{\"__type\":\"AccessDeniedException\",\"Message\":\"User is not authorized\"}");
        var provider = new AwsSecretsManagerProvider(sender);

        Exception? thrown = null;
        WithEnv("AWS_REGION", "us-east-1", () =>
            WithEnv("AWS_ACCESS_KEY_ID", "probe-key", () =>
                WithEnv("AWS_SECRET_ACCESS_KEY", "probe-secret", () =>
                {
                    try { provider.ProbeReadOnly(); }
                    catch (Exception ex) { thrown = ex; }
                })));

        Assert.NotNull(thrown);
        Assert.Equal(SecretProviderErrors.ReasonUnknown, SecretProviderErrors.ReasonCode(thrown!));
        // The message has to name the likely fix, since the code cannot.
        Assert.Contains("secretsmanager:GetSecretValue", thrown!.Message);
        Assert.Contains("secret ARNs", thrown.Message);
    }

    [Fact]
    public void AwsProbe_RequiresTheNotFoundStatus_NotJustTheName()
    {
        // Only HTTP 400 + ResourceNotFoundException is AWS's "you may read, and there is
        // no such secret". A 200 or a 500 carrying that name came from something else.
        var sender = new RecordingSender(HttpStatusCode.OK,
            "{\"__type\":\"ResourceNotFoundException\",\"Message\":\"not found\"}");
        var provider = new AwsSecretsManagerProvider(sender);

        Exception? thrown = null;
        WithEnv("AWS_REGION", "us-east-1", () =>
            WithEnv("AWS_ACCESS_KEY_ID", "probe-key", () =>
                WithEnv("AWS_SECRET_ACCESS_KEY", "probe-secret", () =>
                {
                    try { provider.ProbeReadOnly(); }
                    catch (Exception ex) { thrown = ex; }
                })));

        Assert.NotNull(thrown);
    }

    [Fact]
    public void AwsProbe_ReportsTheKmsDenialOnA400AsUnknownToo()
    {
        // The KMS-key form of AccessDenied arrives as 400, which FromStatus would class
        // as "unavailable" and the GUI would render as "unreachable" - pointing at the
        // network. It is the same ambiguity as the 403 form: unknown, not permission.
        var sender = new RecordingSender(HttpStatusCode.BadRequest,
            "{\"__type\":\"AccessDeniedException\",\"Message\":\"not authorized for kms:Decrypt\"}");
        var provider = new AwsSecretsManagerProvider(sender);

        Exception? thrown = null;
        WithEnv("AWS_REGION", "us-east-1", () =>
            WithEnv("AWS_ACCESS_KEY_ID", "probe-key", () =>
                WithEnv("AWS_SECRET_ACCESS_KEY", "probe-secret", () =>
                {
                    try { provider.ProbeReadOnly(); }
                    catch (Exception ex) { thrown = ex; }
                })));

        Assert.NotNull(thrown);
        Assert.Equal(SecretProviderErrors.ReasonUnknown, SecretProviderErrors.ReasonCode(thrown!));
    }

    [Fact]
    public void AwsProbe_KeepsTheRawServiceTextOutOfTheError()
    {
        // The adapter's standing rule: AWS service error bodies never ride into a
        // surfaced message, because the service echoes the caller ARN there.
        var sender = new RecordingSender(HttpStatusCode.Forbidden,
            "{\"__type\":\"AccessDeniedException\",\"Message\":\"User: arn:aws:iam::123456789012:user/secret-user is not authorized\"}");
        var provider = new AwsSecretsManagerProvider(sender);

        Exception? thrown = null;
        WithEnv("AWS_REGION", "us-east-1", () =>
            WithEnv("AWS_ACCESS_KEY_ID", "probe-key", () =>
                WithEnv("AWS_SECRET_ACCESS_KEY", "probe-secret", () =>
                {
                    try { provider.ProbeReadOnly(); }
                    catch (Exception ex) { thrown = ex; }
                })));

        Assert.NotNull(thrown);
        Assert.DoesNotContain("arn:aws:iam", thrown!.Message);
        Assert.DoesNotContain("secret-user", thrown.Message);
    }

    // ---------------- 1Password ----------------

    // `op whoami` needs a real, signed-in CLI, so this path has no transport seam and
    // no offline success case: the CLI refuses item creation over Connect, and there is
    // no local 1Password backend. What can be pinned without a live account is the
    // declaration (ProbeWritesData == false) plus the fact that the probe spawns
    // `whoami` and not `item create`. The declaration is pinned in
    // SecretProviderCapabilityTests; the process shape is pinned here against a stub
    // executable, so no credential and no network are involved.
    //
    // The provider resolves its binary per instance, not per process (#297 review): a
    // static readonly field would pin the first lookup for the whole test process, so
    // this test's OP_PATH could be ignored depending on what ran first. Constructing the
    // provider inside the WithEnv block is what exercises that.

    [Fact]
    public void OnePasswordProbe_RunsWhoami_NotItemCreate()
    {
        // The stub records the arguments it was called with and exits 0, which models a
        // signed-in CLI. Asserting on the recorded argv is what proves the probe ran
        // `whoami`: a `item create` invocation would show up here as a different command
        // line, and it is the one call that would write into the account.
        string argsLog = Path.Combine(Path.GetTempPath(), "op-probe-args-" + Guid.NewGuid().ToString("N") + ".txt");
        string stub = WriteStubOp(argsLog);
        try
        {
            Exception? thrown = null;
            WithEnv("OP_PATH", stub, () =>
            {
                try { new OnePasswordProvider().ProbeReadOnly(); }
                catch (Exception ex) { thrown = ex; }
            });

            // A zero-exit stub must be treated as a signed-in CLI: no exception.
            Assert.Null(thrown);
            string recorded = File.Exists(argsLog) ? File.ReadAllText(argsLog).Trim() : "";
            // An empty log means the provider ran some other `op` than the stub - the
            // exact failure the per-instance resolution exists to prevent.
            Assert.Equal("whoami", recorded);
            Assert.DoesNotContain("item create", recorded, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(stub); } catch { /* best-effort */ }
            try { File.Delete(argsLog); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void OnePasswordResolver_ReadsOpPathPerInstance_NotOncePerProcess()
    {
        // The seam's own pin: two providers built around a change of OP_PATH must resolve
        // different binaries. With a static cache the second construction would keep the
        // first path, which is what made the probe test order-dependent.
        //
        // Each stub records its own calls, so which binary ran is observable. Availability
        // alone would not prove it: the gate falls back to PATH when OP_PATH is unusable,
        // and a host with `op` installed reports true either way.
        string firstLog = Path.Combine(Path.GetTempPath(), "op-first-" + Guid.NewGuid().ToString("N") + ".txt");
        string secondLog = Path.Combine(Path.GetTempPath(), "op-second-" + Guid.NewGuid().ToString("N") + ".txt");
        string first = WriteStubOp(firstLog);
        string second = WriteStubOp(secondLog);
        try
        {
            WithEnv("OP_PATH", first, () => { OnePasswordProvider p = new(); p.ProbeReadOnly(); });
            // Constructing the second one AFTER the variable moved: no new process, so a
            // static cache would still run the first stub.
            WithEnv("OP_PATH", second, () => { OnePasswordProvider p = new(); p.ProbeReadOnly(); });

            Assert.Equal("whoami", File.ReadAllText(firstLog).Trim());
            Assert.Equal("whoami", File.ReadAllText(secondLog).Trim());
        }
        finally
        {
            try { File.Delete(first); } catch { /* best-effort */ }
            try { File.Delete(second); } catch { /* best-effort */ }
            try { File.Delete(firstLog); } catch { /* best-effort */ }
            try { File.Delete(secondLog); } catch { /* best-effort */ }
        }
    }

    /// <summary>Writes a throwaway .bat that records its argv and exits 0.</summary>
    private static string WriteStubOp(string argsLog)
    {
        string path = Path.Combine(Path.GetTempPath(), "op-probe-stub-" + Guid.NewGuid().ToString("N") + ".bat");
        // %* is the child's full argument string; writing it to the log is the whole point
        // of the stub. Paths contain no spaces here (temp dir + a fixed prefix).
        File.WriteAllText(path, "@echo off\r\necho %* > \"" + argsLog + "\"\r\nexit /b 0\r\n");
        return path;
    }
}
