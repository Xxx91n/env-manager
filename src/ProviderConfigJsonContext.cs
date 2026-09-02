// ProviderConfigJsonContext.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

[JsonSerializable(typeof(SecretProviderManager.ProviderConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
internal sealed partial class ProviderConfigJsonContext : JsonSerializerContext
{
}
