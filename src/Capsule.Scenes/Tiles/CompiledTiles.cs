using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Text.Json;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Tiles;

// The compiled form of a tile map entry's cells, which the build ships in place of its dense tiles and transforms.
// The entry's "blocks" member is one base64 string of this little-endian layout, over the map's blocks of 16 by 16
// cells numbered row-major:
//   1 byte                      the format version, Version
//   ceil(blocks / 8) bytes      a bitmap of the blocks that hold a painted or turned cell, block i at bit i % 8 of byte i / 8
//   ceil(blocks / 8) bytes      a bitmap of those blocks that hold a turned cell, laid out the same way
//   512 bytes per held block    its 256 palette indices as ushort, row-major within the block, in block order
//   256 bytes per turned block  its 256 TileTransform values, in block order
// A held block's cells beyond the map's edge hold 0. A document composed without the build keeps its dense cells,
// which the map reads as authored.
internal static class CompiledTiles
{
    internal const string Key = "blocks";

    // The layout's version. A change to the layout raises it, and a document of another version is refused.
    internal const byte Version = 1;

    private const string Tiles = "tiles";
    private const string Transforms = "transforms";
    private const int BlockBytes = CellBlocks.BlockArea * sizeof(ushort);

    // The well-formed dense cells of every entry whose type is one of tileMaps compiled. An entry whose cells are
    // malformed is left as authored, and its map refuses it at composition naming the defect.
    internal static SceneDocument Compile(SceneDocument document, IReadOnlyCollection<string> tileMaps)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tileMaps);

        SceneDocumentEntry[] entries = document.Entries.ToArray();
        bool compiled = false;
        for (int i = 0; i < entries.Length; i++)
        {
            if (tileMaps.Contains(entries[i].Type, StringComparer.Ordinal)
                && entries[i].Authored is { } members
                && Compile(members) is { } packed)
            {
                entries[i] = entries[i] with { Authored = packed };
                compiled = true;
            }
        }

        return compiled ? new SceneDocument(entries, document.Authored, document.BaseScene, parsed: true) : document;
    }

    // The layout of cells.
    internal static byte[] Encode(CellBlocks cells)
    {
        int count = cells.BlocksWide * cells.BlocksHigh;
        int bitmap = (count + 7) / 8;
        int held = 0;
        int turned = 0;
        for (int index = 0; index < count; index++)
        {
            if (cells.BlockAt(index % cells.BlocksWide, index / cells.BlocksWide) is { } block)
            {
                held++;
                turned += block.HoldsVariants ? 1 : 0;
            }
        }

        byte[] layout = new byte[1 + (2 * bitmap) + (held * BlockBytes) + (turned * CellBlocks.BlockArea)];
        layout[0] = Version;
        Span<byte> values = layout.AsSpan(1 + (2 * bitmap), held * BlockBytes);
        Span<byte> variants = layout.AsSpan(1 + (2 * bitmap) + (held * BlockBytes));
        for (int index = 0; index < count; index++)
        {
            if (cells.BlockAt(index % cells.BlocksWide, index / cells.BlocksWide) is not { } block)
            {
                continue;
            }

            layout[1 + (index / 8)] |= (byte)(1 << (index % 8));
            for (int cell = 0; cell < CellBlocks.BlockArea; cell++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(values[(cell * sizeof(ushort))..], block.Values[cell]);
            }

            values = values[BlockBytes..];
            if (block.HoldsVariants)
            {
                layout[1 + bitmap + (index / 8)] |= (byte)(1 << (index % 8));
                for (int cell = 0; cell < CellBlocks.BlockArea; cell++)
                {
                    variants[cell] = (byte)block.VariantAt(cell);
                }

                variants = variants[CellBlocks.BlockArea..];
            }
        }

        return layout;
    }

    // Decodes a compiled member, the JSON string of the layout, straight into blocks.
    internal static CellBlocks Read(ReadOnlySpan<byte> member, int width, int height) => Read(member, width, height, BitConverter.IsLittleEndian);

    // A bulk read copies each block's values as they lie, and only a little-endian machine takes it.
    internal static CellBlocks Read(ReadOnlySpan<byte> member, int width, int height, bool bulk)
    {
        Utf8JsonReader reader = new(member);
        if (!reader.Read() || reader.TokenType != JsonTokenType.String)
        {
            throw Malformed("it is not a base64 string");
        }

        // A JSON writer escapes base64's '+'. The text is unescaped into a pooled buffer and decoded there.
        byte[] rented = ArrayPool<byte>.Shared.Rent(reader.ValueSpan.Length);
        try
        {
            int written = reader.CopyString(rented);
            if (Base64.DecodeFromUtf8InPlace(rented.AsSpan(0, written), out int length) != OperationStatus.Done)
            {
                throw Malformed("it is not valid base64");
            }

            return Decode(rented.AsSpan(0, length), width, height, bulk);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static CellBlocks Decode(ReadOnlySpan<byte> layout, int width, int height, bool bulk)
    {
        if (layout.IsEmpty || layout[0] != Version)
        {
            throw new ArgumentException(
                $"blocks is compiled in format version {(layout.IsEmpty ? "none" : layout[0])}, and this engine reads version {Version}. Rebuild the assets.",
                Key);
        }

        CellBlocks cells = new(width, height);
        int count = cells.BlocksWide * cells.BlocksHigh;
        int bitmap = (count + 7) / 8;
        if (layout.Length < 1 + (2 * bitmap))
        {
            throw Malformed($"it holds {layout.Length} bytes, short of the two bitmaps of {count} blocks");
        }

        ReadOnlySpan<byte> heldBits = layout.Slice(1, bitmap);
        ReadOnlySpan<byte> turnedBits = layout.Slice(1 + bitmap, bitmap);
        int held = 0;
        int turned = 0;
        for (int index = 0; index < count; index++)
        {
            bool holds = Bit(heldBits, index);
            bool turns = Bit(turnedBits, index);
            if (turns && !holds)
            {
                throw Malformed($"block {index} is turned but not held");
            }

            held += holds ? 1 : 0;
            turned += turns ? 1 : 0;
        }

        ReadOnlySpan<byte> values = layout[(1 + (2 * bitmap))..];
        if (values.Length != (held * BlockBytes) + (turned * CellBlocks.BlockArea))
        {
            throw Malformed($"it holds {layout.Length} bytes where {held} blocks, {turned} of them turned, take {1 + (2 * bitmap) + (held * BlockBytes) + (turned * CellBlocks.BlockArea)}");
        }

        ReadOnlySpan<byte> variants = values[(held * BlockBytes)..];
        for (int index = 0; index < count; index++)
        {
            if (!Bit(heldBits, index))
            {
                continue;
            }

            int column = index % cells.BlocksWide;
            int row = index / cells.BlocksWide;
            ReadOnlySpan<byte> turns = Bit(turnedBits, index) ? variants[..CellBlocks.BlockArea] : default;
            CellBlocks.Block block = cells.Add(column, row);
            block.Fill(values[..BlockBytes], turns, bulk);
            values = values[BlockBytes..];
            variants = turns.IsEmpty ? variants : variants[CellBlocks.BlockArea..];
            RequireInside(block, column, row, width, height);
        }

        return cells;
    }

    private static bool Bit(ReadOnlySpan<byte> bits, int index) => (bits[index / 8] & (1 << (index % 8))) != 0;

    // A block on the map's right or bottom edge holds nothing past it.
    private static void RequireInside(CellBlocks.Block block, int column, int row, int width, int height)
    {
        int across = Math.Min(CellBlocks.BlockSize, width - (column << CellBlocks.BlockShift));
        int down = Math.Min(CellBlocks.BlockSize, height - (row << CellBlocks.BlockShift));
        if (across == CellBlocks.BlockSize && down == CellBlocks.BlockSize)
        {
            return;
        }

        for (int cell = 0; cell < CellBlocks.BlockArea; cell++)
        {
            if ((cell & (CellBlocks.BlockSize - 1)) >= across || (cell >> CellBlocks.BlockShift) >= down)
            {
                if (block.Values[cell] != 0 || block.VariantAt(cell) != 0)
                {
                    throw Malformed($"block ({column}, {row}) paints cell {cell}, which lies beyond the map's edge");
                }
            }
        }
    }

    // The members with dense cells replaced by the compiled member, or null when they author no dense cells or
    // malformed ones.
    private static AuthoredObject? Compile(AuthoredObject members)
    {
        if (members.TryGetValue(Key, out _)
            || !members.TryGetValue(Tiles, out ReadOnlyMemory<byte> tiles)
            || !members.TryGetValue("width", out ReadOnlyMemory<byte> wide) || !AuthoredValue.TryInt(wide.Span, out int width) || width <= 0
            || !members.TryGetValue("height", out ReadOnlyMemory<byte> high) || !AuthoredValue.TryInt(high.Span, out int height) || height <= 0
            || Dense(tiles.Span, (long)width * height, CellBlocks.MaxValue) is not { } values)
        {
            return null;
        }

        byte[]? facings = null;
        if (members.TryGetValue(Transforms, out ReadOnlyMemory<byte> turns) && AuthoredValue.Kind(turns.Span) != JsonValueKind.Null)
        {
            if (Dense(turns.Span, values.Length, TileTransforms.Count - 1) is not { } read)
            {
                return null;
            }

            facings = [.. read.Select(static turn => (byte)turn)];
        }

        byte[] layout = Encode(CellBlocks.FromDense(width, height, values, facings));
        ArrayBufferWriter<byte> written = new();
        using (Utf8JsonWriter writer = new(written))
        {
            writer.WriteStartObject();
            foreach (string key in members.Keys)
            {
                if (string.Equals(key, Tiles, StringComparison.Ordinal))
                {
                    writer.WriteBase64String(Key, layout);
                }
                else if (!string.Equals(key, Transforms, StringComparison.Ordinal))
                {
                    members.TryGetValue(key, out ReadOnlyMemory<byte> value);
                    writer.WritePropertyName(key);
                    writer.WriteRawValue(value.Span, skipInputValidation: true);
                }
            }

            writer.WriteEndObject();
        }

        return AuthoredObject.Parse(written.WrittenMemory.ToArray());
    }

    // The ints of an array of count integers, each in 0..max, or null when it is anything else.
    private static int[]? Dense(ReadOnlySpan<byte> array, long count, int max)
    {
        Utf8JsonReader reader = new(array);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            return null;
        }

        List<int> read = new((int)Math.Min(count, 1 << 20));
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int value) || value < 0 || value > max || read.Count == count)
            {
                return null;
            }

            read.Add(value);
        }

        return read.Count == count ? [.. read] : null;
    }

    private static ArgumentException Malformed(string defect) =>
        new($"blocks is not the compiled form this engine's build writes: {defect}. Rebuild the assets.", Key);
}
