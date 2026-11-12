// HttpClientSender.cs - production transport for signed AWS requests
// License: Apache-2.0

using System;
using System.Net.Http;

namespace EnvManager.Secrets.Providers;

internal sealed class HttpClientSender : IAwsHttpSender
{
    internal static readonly HttpClientSender Instance = new HttpClientSender();

    public HttpResponseMessage Send(HttpRequestMessage request, TimeSpan timeout)
    {
        // A fresh client per call: the providers are invoked from short-lived CLI
        // processes and a shared handler would outlive the process that owns it.
        //
        // Redirects are off. A signed request carries the session token and, on a write,
        // the plaintext secret in its body; following a redirect would hand both to
        // whatever host the Location header names, and that host never passed the
        // endpoint check. A local emulator has no reason to redirect.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new System.Net.Http.HttpClient(handler);
        client.Timeout = timeout;
        return client.SendAsync(request).GetAwaiter().GetResult();
    }
}
