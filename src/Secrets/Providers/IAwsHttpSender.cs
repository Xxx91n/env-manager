// IAwsHttpSender.cs - transport seam for signed AWS requests
// License: Apache-2.0

// Tests inject a fake so the wire bytes are asserted without any network. There is
// deliberately no environment variable that buys this seam in production: a switchable
// signing endpoint would let a planted variable hand the process forged temporary
// credentials.

using System;
using System.Net.Http;

namespace EnvManager.Secrets.Providers;

internal interface IAwsHttpSender
{
    HttpResponseMessage Send(HttpRequestMessage request, TimeSpan timeout);
}
