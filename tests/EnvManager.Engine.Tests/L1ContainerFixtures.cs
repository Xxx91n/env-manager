// L1ContainerFixtures.cs - shared emulator containers + localhost Connect mock (issue 15)
// License: Apache-2.0
//
// One lazily-started container per emulator backend, shared across every test in the
// run (IAsyncLifetime-free static caching keeps the matrix to 3 container starts).
// Vault dev server has no Testcontainers .NET module (verified 2026-09-03: the NuGet
// package Testcontainers.Vault does not exist), so it uses a generic container with
// the documented dev-mode env contract. LocalStack is pinned to 4.4.0 - the last
// token-free community image (2026-03 unified image requires LOCALSTACK_AUTH_TOKEN).

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.LocalStack;
using Testcontainers.LowkeyVault;

namespace EnvManager.Engine.Tests;

internal static class L1ContainerFixtures
{
    // ---- pinned images (issue 15 image pinning; verified live 2026-09-03) ----

    internal const string VaultImage = "hashicorp/vault:1.20.4";
    internal const string LocalStackImage = "localstack/localstack:4.4.0";
    internal const string LowkeyVaultImage = "nagyesta/lowkey-vault:4.0.0-ubi9-minimal";

    // ---- HashiCorp Vault dev server (generic container; no official .NET module) ----

    private static readonly Lazy<VaultDevContainer> Vault = new(() =>
    {
        var token = "em-l1-root-token";
        var container = new ContainerBuilder(VaultImage)
            .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", token)
            .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
            // the image's default entrypoint is "docker-entrypoint.sh server"; dev mode
            // additionally needs the -dev flag or vault waits on a config file forever
            .WithCommand("server", "-dev")
            .WithPortBinding(8200, true)
            // CI run 33845154578 (44m hang) pinned the failure on the HTTP wait strategy
            // with a query-string health path; no custom strategy = Testcontainers' default
            // wait (container running), then the bounded manual health poll below decides
            // readiness so a flaky endpoint can never wedge the job.
            .Build();
        container.StartAsync().GetAwaiter().GetResult();
        // bounded manual health poll (dev server is typically healthy in <5s)
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var port = container.GetMappedPublicPort(8200);
                using var resp = client.GetAsync($"http://{container.Hostname}:{port}/v1/sys/health?standbyok=true&sealedcode=200&uninitcode=200").GetAwaiter().GetResult();
                // 200 (active) or 429/472/473 (standby/uninit variants) all mean the server is up
                if ((int)resp.StatusCode is 200 or 429 or 472 or 473)
                {
                    return new VaultDevContainer(container, token);
                }
            }
            catch (Exception) { }
            System.Threading.Thread.Sleep(1000);
        }
        throw new InvalidOperationException("vault dev server did not become healthy within 3 minutes");
    });

    internal sealed record VaultDevContainer(IContainer Container, string RootToken)
    {
        public string Addr
        {
            get
            {
                var host = Container.Hostname;
                var port = Container.GetMappedPublicPort(8200);
                return $"http://{host}:{port}";
            }
        }
    }

    internal static VaultDevContainer StartVaultDevServer()
    {
        L1MatrixAffinity.AssertAffinityOrSkip("vault-kv2 (dev server container)");
        return Vault.Value;
    }

    // ---- LocalStack (AWS Secrets Manager emulator) ----

    private static readonly Lazy<LocalStackContainer> LocalStack = new(() =>
    {
        var container = new LocalStackBuilder(LocalStackImage)
            .WithEnvironment("SERVICES", "secretsmanager")
            .Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container;
    });

    internal static LocalStackContainer StartLocalStack()
    {
        L1MatrixAffinity.AssertAffinityOrSkip("aws-secretsmanager (LocalStack container)");
        return LocalStack.Value;
    }

    // ---- Lowkey Vault (Azure Key Vault test double; also fakes the AAD token
    //      endpoint on its http token port, so the provider's managed-identity path
    //      is driven through the IDENTITY_ENDPOINT production seam) ----

    private static readonly Lazy<LowkeyVaultContainer> LowkeyVault = new(() =>
    {
        var container = new LowkeyVaultBuilder(LowkeyVaultImage)
            .Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container;
    });

    internal static LowkeyVaultContainer StartLowkeyVault()
    {
        L1MatrixAffinity.AssertAffinityOrSkip("azure-keyvault (Lowkey Vault container)");
        return LowkeyVault.Value;
    }

    // ---- 1Password Connect REST mock (in-process; only the two endpoints the op
    //      CLI hits for the flows OnePasswordProvider uses: create item, get item
    //      field, delete item) ----

    internal static readonly Lazy<OpConnectMock> ConnectMock = new(() => OpConnectMock.Start());
}
