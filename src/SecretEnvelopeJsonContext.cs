// SecretEnvelopeJsonContext.cs - JSON source-generation context for SecretEnvelope (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

[JsonSerializable(typeof(SecretEnvelope))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
internal sealed partial class SecretEnvelopeJsonContext : JsonSerializerContext
{
}

