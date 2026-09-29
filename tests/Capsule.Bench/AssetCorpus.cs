using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Capsule.Build.Textures;

namespace Capsule.Bench;

// The synthetic game the `assets` bench builds: a large 2D metroidvania's authoring tree under a logic
// project wired to this engine clone in source mode, and a build project running a toy importer. Every
// file is a pure function of its kind, its index and an edit number. Edit 0 is the canonical corpus, and
// the bench's edit cases write edits 1, 2, 3... so each run really changes the file.
internal sealed class AssetCorpus
{
    // Bump it whenever what the generator writes changes. The next run deletes the corpus and writes it
    // again, and records name it, so a new corpus reads as one and not as a jump in the history.
    internal const int Version = 3;

    private const string VersionFile = "corpus.version";

    private const int SpritesPerAtlas = 600;
    private const int SheetsPerAtlas = 50;
    private const int TextureZones = 20;
    private const int TexturesPerZone = 200;
    private const int MaskSets = 3;
    private const int MasksPerSet = 100;
    private const int Areas = 10;
    private const int ScenesPerArea = 40;
    private const int RoomsPerWing = 40;
    private const int EffectGroups = 10;
    private const int EffectsPerGroup = 100;
    private const int Shaders = 6;

    // A scene's map and a room's grid, in 16-pixel tiles: a few screens of a large room.
    private const int MapWidth = 256;
    private const int MapHeight = 144;
    private const int TileTypes = 40;
    private const int TilesetColumns = 16;

    private static readonly string[] Atlases = ["actors", "enemies", "bosses", "npcs", "items", "effects", "props", "ui"];

    private readonly string _engineRoot;

    internal string Root { get; }

    private AssetCorpus(string engineRoot, string root)
    {
        _engineRoot = engineRoot;
        Root = root;
    }

    internal string LogicProject => Path.Combine(Root, "src", "AssetCorpus.Game", "AssetCorpus.Game.csproj");

    internal string Assets => Path.Combine(Root, "src", "AssetCorpus.Game", "Assets");

    internal static AssetCorpus Ensure(string engineRoot)
    {
        AssetCorpus corpus = new(engineRoot, Path.Combine(engineRoot, "artifacts", "bench", "asset-corpus"));
        string stamp = Path.Combine(corpus.Root, VersionFile);
        if (File.Exists(stamp) && File.ReadAllText(stamp) == Version.ToString(CultureInfo.InvariantCulture))
        {
            return corpus;
        }

        if (Directory.Exists(corpus.Root))
        {
            Directory.Delete(corpus.Root, recursive: true);
        }

        Console.WriteLine($"bench: generating the asset corpus under {corpus.Root}");
        corpus.Generate();

        // Written last, so a generation that stopped part way is generated again.
        File.WriteAllText(stamp, Version.ToString(CultureInfo.InvariantCulture));

        return corpus;
    }

    // ==== The tree ====

    private void Generate()
    {
        AssetCorpusProjects.Write(Root, Path.GetRelativePath(Root, _engineRoot).Replace('\\', '/'));

        foreach (string atlas in Atlases)
        {
            WriteText($"Atlases/{atlas}.atlas.json", "{}\n");
            WriteText($"Sprites/{atlas}/.config.json", $$"""{ "texture": { "atlas": "{{atlas}}" } }""" + "\n");
        }

        Parallel.For(0, Atlases.Length * SpritesPerAtlas, index => WriteSprite(index / SpritesPerAtlas, index % SpritesPerAtlas, 0));
        Parallel.For(0, Atlases.Length * SheetsPerAtlas, index => WriteSheet(index / SheetsPerAtlas, index % SheetsPerAtlas));
        Parallel.For(0, TextureZones * TexturesPerZone, index => WriteTexture(index, 0));

        WriteText("Masks/.config.json", """{ "texture": { "format": "r8" } }""" + "\n");
        Parallel.For(0, MaskSets * MasksPerSet, index => WriteMask(index, 0));

        Parallel.For(0, Areas, WriteTileset);
        Parallel.For(0, Areas * ScenesPerArea, index => WriteScene(index, 0));
        Parallel.For(0, Areas, index => WritePalette(index, 0));
        Parallel.For(0, Areas * RoomsPerWing, index => WriteRoom(index, 0));

        Parallel.For(0, EffectGroups * EffectsPerGroup, WriteEffect);
        string music = Path.Combine(_engineRoot, "samples", "MinimalGame", "src", "MinimalGame.Game", "Assets", "Audio", "Music");
        foreach (string track in (string[])["room", "title"])
        {
            CopySample(Path.Combine(music, track + ".ogg"), $"Audio/Music/{track}.ogg");
            CopySample(Path.Combine(music, track + ".ogg"), $"Audio/Music/Boss/{track}.ogg");
        }

        for (int index = 0; index < Shaders; index++)
        {
            WriteShader(index, 0);
        }

        string fonts = Path.Combine(_engineRoot, "samples", "MinimalGame", "src", "MinimalGame.Game", "Assets", "Fonts");
        string description = File.ReadAllText(Path.Combine(fonts, "menu.fnt"));
        foreach (string font in (string[])["menu", "hud"])
        {
            WriteText($"Fonts/{font}.fnt", description.Replace("file=\"menu.png\"", $"file=\"{font}.png\"", StringComparison.Ordinal));
            CopySample(Path.Combine(fonts, "menu.png"), $"Fonts/{font}.png");
        }
    }

    // A small packed sprite, 32 to 160 texels a side: a shaded blob on transparency.
    internal void WriteSprite(int atlas, int index, int edit)
    {
        (int width, int height) = SpriteSize(atlas, index);
        Random paint = new(Seed(1, (atlas * SpritesPerAtlas) + index, edit));
        byte red = (byte)paint.Next(256), green = (byte)paint.Next(256), blue = (byte)paint.Next(256);

        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = ((x + 0.5f) / width) - 0.5f;
                float dy = ((y + 0.5f) / height) - 0.5f;
                float falloff = 1f - (4f * ((dx * dx) + (dy * dy)));
                int at = ((y * width) + x) * 4;
                if (falloff > 0f)
                {
                    pixels[at] = (byte)(red * falloff);
                    pixels[at + 1] = (byte)(green * falloff);
                    pixels[at + 2] = (byte)(blue * falloff);
                    pixels[at + 3] = 255;
                }
            }
        }

        WritePng($"Sprites/{Atlases[atlas]}/{SpriteName(index)}.png", pixels, width, height, 4);
    }

    // An idle clip over the sprite's four quarters.
    private void WriteSheet(int atlas, int index)
    {
        (int width, int height) = SpriteSize(atlas, index);
        width /= 2;
        height /= 2;
        string name = SpriteName(index);

        StringBuilder sheet = new();
        sheet.Append(CultureInfo.InvariantCulture, $"{{\n  \"formatVersion\": 1,\n  \"texture\": \"sprites/{Atlases[atlas]}/{name}.png\",\n  \"frames\": [\n");
        for (int frame = 0; frame < 4; frame++)
        {
            string separator = frame < 3 ? "," : string.Empty;
            sheet.Append(CultureInfo.InvariantCulture, $"    {{ \"name\": \"idle-{frame}\", \"x\": {frame % 2 * width}, \"y\": {frame / 2 * height}, \"width\": {width}, \"height\": {height}, \"pivot\": [{width / 2}, {height}] }}{separator}\n");
        }

        sheet.Append("  ],\n  \"clips\": [\n    { \"name\": \"idle\", \"loop\": true, \"frames\": [\n");
        for (int frame = 0; frame < 4; frame++)
        {
            string separator = frame < 3 ? "," : string.Empty;
            sheet.Append(CultureInfo.InvariantCulture, $"      {{ \"frame\": \"idle-{frame}\", \"ticks\": 8 }}{separator}\n");
        }

        sheet.Append("    ] }\n  ]\n}\n");
        WriteText($"Sprites/{Atlases[atlas]}/{name}.sheet.json", sheet.ToString());
    }

    // An unpacked texture, each side 256 to 1024 texels and most of them small. Every third is noise,
    // which does not compress.
    internal void WriteTexture(int index, int edit)
    {
        Random shape = new(Seed(2, index, 0));
        int width = TextureSide(shape);
        int height = TextureSide(shape);
        Random paint = new(Seed(2, index, edit));
        byte[] pixels = new byte[width * height * 4];

        if (index % 3 == 0)
        {
            paint.NextBytes(pixels);
        }
        else
        {
            // Flat 16-texel blocks, which compress several times over as painted art does.
            int a = paint.Next(1, 7), b = paint.Next(1, 7), c = paint.Next(256);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int at = ((y * width) + x) * 4;
                    pixels[at] = (byte)(((x >> 4) * a) + c);
                    pixels[at + 1] = (byte)(((y >> 4) * b) + c);
                    pixels[at + 2] = (byte)(((x + y) >> 5) ^ c);
                    pixels[at + 3] = 255;
                }
            }
        }

        WritePng($"Textures/zone-{index / TexturesPerZone:D2}/texture-{index % TexturesPerZone:D3}.png", pixels, width, height, 4);
    }

    // A 512-texel r8 mask: a radial ramp with grain.
    internal void WriteMask(int index, int edit)
    {
        const int Side = 512;
        Random paint = new(Seed(3, index, edit));
        int centerX = paint.Next(Side), centerY = paint.Next(Side);
        byte[] values = new byte[Side * Side];
        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                int distance = Math.Abs(x - centerX) + Math.Abs(y - centerY);
                values[(y * Side) + x] = (byte)Math.Clamp(255 - (distance / 2) + paint.Next(-8, 9), 0, 255);
            }
        }

        WritePng($"Masks/set-{index / MasksPerSet}/mask-{index % MasksPerSet:D3}.png", values, Side, Side, 1);
    }

    // One area's tileset, 16 by 16 cells of 16 texels.
    private void WriteTileset(int area)
    {
        const int Side = TilesetColumns * 16;
        Random paint = new(Seed(4, area, 0));
        byte[] pixels = new byte[Side * Side * 4];
        paint.NextBytes(pixels);
        WritePng($"Tilesets/tileset-{area:D2}.png", pixels, Side, Side, 4);
    }

    // A room as its editor exports it: a collision map and a decoration map over the area's tileset,
    // one grid row per line, a few hundred KB of JSON.
    internal void WriteScene(int index, int edit)
    {
        int area = index / ScenesPerArea;
        Random map = new(Seed(5, index, edit));

        StringBuilder scene = new();
        scene.Append("{\n  \"formatVersion\": 7,\n  \"entities\": [\n");
        AppendTileMap(scene, 1, -10, $"tilesets/tileset-{area:D2}.png", "solid", map);
        scene.Append(",\n");
        AppendTileMap(scene, 2, 10, $"tilesets/tileset-{area:D2}.png", null, map);
        scene.Append("\n  ],\n  \"nextEntityId\": 3\n}\n");

        WriteText($"Scenes/area-{area:D2}/room-{index % ScenesPerArea:D3}.scene.json", scene.ToString());
    }

    private static void AppendTileMap(StringBuilder scene, int id, int zIndex, string texture, string? layer, Random map)
    {
        scene.Append(CultureInfo.InvariantCulture, $"    {{\n      \"id\": {id},\n      \"type\": \"tile-map\",\n      \"x\": 0,\n      \"y\": 0,\n      \"zIndex\": {zIndex},\n");
        scene.Append(CultureInfo.InvariantCulture, $"      \"properties\": {{\n        \"tileSize\": 16,\n        \"width\": {MapWidth},\n        \"height\": {MapHeight},\n        \"texture\": \"{texture}\",\n        \"columns\": {TilesetColumns},\n");
        scene.Append("        \"tileTypes\": [\n          { \"type\": \"empty\" }");
        for (int type = 1; type <= TileTypes; type++)
        {
            string collides = layer is not null && type <= 8 ? $", \"layer\": \"{layer}\"" : string.Empty;
            scene.Append(CultureInfo.InvariantCulture, $",\n          {{ \"type\": \"t{type}\", \"cell\": {(type * 7) % (TilesetColumns * TilesetColumns)}{collides} }}");
        }

        scene.Append("\n        ],\n        \"tiles\": [\n");
        AppendGrid(scene, map, "          ");
        scene.Append("        ]\n      }\n    }");
    }

    // Ground along the bottom and scattered tiles above it, one comma-separated row per line.
    private static void AppendGrid(StringBuilder text, Random map, string indent)
    {
        for (int y = 0; y < MapHeight; y++)
        {
            text.Append(indent);
            for (int x = 0; x < MapWidth; x++)
            {
                int tile = y >= MapHeight - 4 ? map.Next(1, 5) : map.Next(100) < 8 ? map.Next(1, TileTypes + 1) : 0;
                text.Append(tile);
                if (x < MapWidth - 1 || y < MapHeight - 1)
                {
                    text.Append(", ");
                }
            }

            text.Append('\n');
        }
    }

    // The toy importer's tileset: the texture it cuts from and one line per tile type.
    internal void WritePalette(int wing, int edit)
    {
        Random types = new(Seed(6, wing, edit));
        StringBuilder palette = new();
        palette.Append(CultureInfo.InvariantCulture, $"texture tilesets/tileset-{wing:D2}.png\ncolumns {TilesetColumns}\n");
        for (int type = 1; type <= TileTypes; type++)
        {
            palette.Append(CultureInfo.InvariantCulture, $"t{type} {types.Next(TilesetColumns * TilesetColumns)}{(types.Next(4) == 0 ? " solid" : string.Empty)}\n");
        }

        WriteText($"Rooms/Palettes/palette-{wing:D2}.palette", palette.ToString());
    }

    // The toy importer's source: the palette it names, then the grid.
    internal void WriteRoom(int index, int edit)
    {
        int wing = index / RoomsPerWing;
        StringBuilder room = new();
        room.Append(CultureInfo.InvariantCulture, $"palette Rooms/Palettes/palette-{wing:D2}.palette\nsize {MapWidth} {MapHeight}\n");
        AppendGrid(room, new Random(Seed(7, index, edit)), string.Empty);

        WriteText($"Rooms/wing-{wing:D2}/room-{index % RoomsPerWing:D3}.room", room.ToString());
    }

    // A short 16-bit mono effect: a decaying tone over grain.
    private void WriteEffect(int index)
    {
        const int Rate = 22050;
        Random sound = new(Seed(8, index, 0));
        int samples = Rate * sound.Next(80, 600) / 1000;
        double pitch = sound.Next(200, 2000);

        byte[] wav = new byte[44 + (samples * 2)];
        Span<byte> header = wav;
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], wav.Length - 8);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], Rate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], Rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double envelope = 1d - ((double)i / samples);
            double value = (Math.Sin(2d * Math.PI * pitch * i / Rate) * 0.6d) + ((sound.NextDouble() - 0.5d) * 0.2d);
            BinaryPrimitives.WriteInt16LittleEndian(header[(44 + (i * 2))..], (short)(value * envelope * short.MaxValue));
        }

        File.WriteAllBytes(AssetFile($"Audio/Effects/group-{index / EffectsPerGroup:D2}/sfx-{index % EffectsPerGroup:D3}.wav"), wav);
    }

    // The sample's duotone shader, its ramp scaled by the index and the edit.
    internal void WriteShader(int index, int edit)
    {
        string scale = (1d + (index * 0.01d) + (edit * 0.001d)).ToString("F3", CultureInfo.InvariantCulture);
        WriteText($"Shaders/shader-{index}.fx", $$"""
            // Maps each texel's brightness onto a ramp from Shadow to Light.
            float4 Shadow;
            float4 Light;

            float4 Fragment(SpritePixel pixel)
            {
                float4 color = pixel.Texel * pixel.Tint;
                float shade = dot(color.rgb, float3(0.299, 0.587, 0.114)) / max(color.a, 0.0001);
                return lerp(Shadow, Light, saturate(shade * {{scale}})) * color.a;
            }

            """);
    }

    // ==== Files ====

    private static string SpriteName(int index) => $"sprite-{index:D3}";

    private static (int Width, int Height) SpriteSize(int atlas, int index)
    {
        Random shape = new(Seed(1, (atlas * SpritesPerAtlas) + index, 0));

        return (shape.Next(32, 161), shape.Next(32, 161));
    }

    private static int TextureSide(Random shape) => shape.Next(100) switch
    {
        < 55 => 256,
        < 80 => 512,
        < 92 => 768,
        _ => 1024,
    };

    // Distinct per kind, index and edit, and the same on every machine.
    private static int Seed(int kind, int index, int edit) => unchecked((kind * 73_856_093) ^ (index * 19_349_663) ^ (edit * 83_492_791));

    private void CopySample(string source, string assetPath) => File.Copy(source, AssetFile(assetPath), overwrite: true);

    private void WriteText(string assetPath, string text) => File.WriteAllText(AssetFile(assetPath), text.ReplaceLineEndings("\n"));

    private string AssetFile(string assetPath)
    {
        string path = Path.Combine(Assets, assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        return path;
    }

    // Encoded as the build encodes what it ships.
    private void WritePng(string assetPath, byte[] pixels, int width, int height, int channels)
    {
        using FileStream file = File.Create(AssetFile(assetPath));
        PngWriter.Write(pixels, width, height, channels, file);
    }
}
