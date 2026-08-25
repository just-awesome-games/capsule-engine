using System.Text.Json.Serialization;

namespace Capsule.Levels.Cli.Tiled;

[JsonSerializable(typeof(TiledMap))]
[JsonSerializable(typeof(TiledTileset))]
internal sealed partial class TiledJsonContext : JsonSerializerContext;
