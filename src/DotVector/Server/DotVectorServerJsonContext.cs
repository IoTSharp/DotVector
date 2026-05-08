using System.Text.Json.Serialization;

namespace DotVector.Server;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DatabaseCatalogDocument))]
internal sealed partial class DotVectorServerJsonContext : JsonSerializerContext
{
}
