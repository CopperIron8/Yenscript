using System.Text.Json.Serialization;

namespace Yenscript.Cli.Io;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WasmMetadata.Document))]
internal partial class MetadataJsonContext : JsonSerializerContext;
