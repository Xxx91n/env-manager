// SecretEnvelopeJsonContext.cs - JSON source-generation context for SecretEnvelope (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

[JsonSerializable(typeof(SecretEnvelope))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
internal sealed partial class SecretEnvelopeJsonContext : JsonSerializerContext
{
}

