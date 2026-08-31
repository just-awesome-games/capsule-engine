using System.Text.Json.Serialization;

namespace Capsule.Cli.Tiled;

[JsonSerializable(typeof(TiledMap))]
[JsonSerializable(typeof(TiledTileset))]
internal sealed partial class TiledJsonContext : JsonSerializerContext;
