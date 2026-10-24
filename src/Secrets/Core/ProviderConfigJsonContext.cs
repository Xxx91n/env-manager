using EnvManager.Secrets.Manager;
// ProviderConfigJsonContext.cs - secret provider architecture
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager.Secrets.Core;

[JsonSerializable(typeof(SecretProviderManager.ProviderConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
internal sealed partial class ProviderConfigJsonContext : JsonSerializerContext
{
}
