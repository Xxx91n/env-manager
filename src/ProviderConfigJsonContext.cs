// ProviderConfigJsonContext.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

[JsonSerializable(typeof(SecretProviderManager.ProviderConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
internal sealed partial class ProviderConfigJsonContext : JsonSerializerContext
{
}
