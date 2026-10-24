// SecretEnvelopeJsonContext.cs - JSON source-generation context for SecretEnvelope
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager.Secrets.Core;

[JsonSerializable(typeof(SecretEnvelope))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
internal sealed partial class SecretEnvelopeJsonContext : JsonSerializerContext
{
}

