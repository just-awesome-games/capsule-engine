using Capsule.Physics;
using static Capsule.Tiles.TileGridValidation;

namespace Capsule.Tiles;

// The looks a palette's rule tile types pick between. Each look is a slot of its own after the palette's entries. A
// cell holds the slot it draws and the draw reads it as it reads any entry, while everything else maps the slot back
// to its entry. A look depends only on the terrains around its cell, which belong to their entries and never to a
// look. Picking a look changes nothing a neighbour reads: a paint repicks at most the eight cells around it, and the
// result never depends on the order cells were painted in.
internal sealed class TileRules
{
    // The cell each side names, by its bit in TileSides.
    private static readonly (int X, int Y)[] Offsets = [(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)];

    // Each corner's bit and the two sides it needs joined to join.
    private static readonly (int Corner, int Sides)[] Corners =
    [
        ((int)TileSides.UpRight, (int)(TileSides.Up | TileSides.Right)),
        ((int)TileSides.DownRight, (int)(TileSides.Down | TileSides.Right)),
        ((int)TileSides.DownLeft, (int)(TileSides.Down | TileSides.Left)),
        ((int)TileSides.UpLeft, (int)(TileSides.Up | TileSides.Left)),
    ];

    // By slot: the entry it draws for, that entry's index, and the terrain it counts as, 0 for none.
    private readonly TileType[] _slots;
    private readonly int[] _entries;
    private readonly int[] _terrains;

    // By slot: the rules its entry picks with, or null for an entry without rules.
    private readonly Rules?[] _rules;

    // By slot past the palette's entries: the cell that look draws.
    private readonly int[] _lookCells;

    private TileRules(TileType[] slots, int[] entries, int[] terrains, Rules?[] rules, int[] lookCells)
    {
        _slots = slots;
        _entries = entries;
        _terrains = terrains;
        _rules = rules;
        _lookCells = lookCells;
    }

    // Every slot's entry: the palette itself, then the entry each look draws for.
    internal TileType[] Slots => _slots;

    // The rules of a palette that has any, checked, or null for a palette with none.
    internal static TileRules? Of(TileType[] palette)
    {
        Dictionary<string, int> terrains = new(StringComparer.Ordinal);
        int looks = 0;
        for (int i = 0; i < palette.Length; i++)
        {
            TileType tileType = palette[i];
            if (tileType.Terrain is { } terrain)
            {
                terrains.TryAdd(Named(terrain, tileType, i, "terrain"), terrains.Count + 1);
            }

            if (tileType.Rules is null)
            {
                if (tileType.Joins is not null)
                {
                    throw Malformed($"{Palette(tileType, i)} joins \"{tileType.Joins}\" but has no rules. Add rules, or drop joins.");
                }

                continue;
            }

            looks += tileType.Rules.Length;
        }

        if (!Array.Exists(palette, static tileType => tileType.Rules is not null))
        {
            return null;
        }

        int count = palette.Length + looks;
        if (count > CellBlocks.MaxValue + 1)
        {
            throw Malformed(
                $"tileTypes has {palette.Length} entries and {looks} rules, more than the {CellBlocks.MaxValue + 1} looks a grid can index. Drop entries no tile uses, or split the palette across maps.");
        }

        TileType[] slots = new TileType[count];
        int[] entries = new int[count];
        int[] ids = new int[count];
        Rules?[] rules = new Rules?[count];
        int[] lookCells = new int[looks];
        int next = palette.Length;
        for (int i = 0; i < palette.Length; i++)
        {
            TileType tileType = palette[i];
            int terrain = tileType.Terrain is { } named ? terrains[named] : 0;
            (slots[i], entries[i], ids[i]) = (tileType, i, terrain);
            if (tileType.Rules is not { } authored)
            {
                continue;
            }

            Rules picks = RulesOf(tileType, i, authored, terrains, next);
            rules[i] = picks;
            for (int rule = 0; rule < authored.Length; rule++, next++)
            {
                (slots[next], entries[next], ids[next], rules[next]) = (tileType, i, terrain, picks);
                lookCells[next - palette.Length] = authored[rule].Cell;
            }
        }

        return new TileRules(slots, entries, ids, rules, lookCells);
    }

    // The neighbour of (x, y) on a side, by its bit's position in TileSides.
    internal static (int X, int Y) Beside(int x, int y, int side) => (x + Offsets[side].X, y + Offsets[side].Y);

    internal int EntryOf(int slot) => _entries[slot];

    // The cell the look at a slot past the palette's entries draws.
    internal int LookCell(int slot) => _lookCells[slot - (_slots.Length - _lookCells.Length)];

    internal int TerrainOf(int slot) => _terrains[slot];

    // Picks the look of every cell painted with a rule entry, in cells no one has read yet. A rule entry's own index
    // never stays in a cell.
    internal void Settle(CellBlocks cells)
    {
        for (int by = 0; by < cells.BlocksHigh; by++)
        {
            for (int bx = 0; bx < cells.BlocksWide; bx++)
            {
                if (cells.BlockAt(bx, by) is null)
                {
                    continue;
                }

                int right = Math.Min(cells.Width, (bx + 1) << CellBlocks.BlockShift);
                int bottom = Math.Min(cells.Height, (by + 1) << CellBlocks.BlockShift);
                for (int y = by << CellBlocks.BlockShift; y < bottom; y++)
                {
                    for (int x = bx << CellBlocks.BlockShift; x < right; x++)
                    {
                        int value = cells.ValueAt(x, y);
                        if (_rules[value] is not { } rules)
                        {
                            continue;
                        }

                        int turn = cells.VariantAt(x, y);
                        if (turn != 0)
                        {
                            long index = ((long)y * cells.Width) + x;
                            throw new ArgumentException(
                                $"tiles[{index}] is the rule tile \"{_slots[value].Name}\", turned by transforms[{index}] = {turn}. A rule tile picks its own look; drop its transform.",
                                "transforms");
                        }

                        cells.Set(x, y, Pick(cells, x, y, rules), 0);
                    }
                }
            }
        }
    }

    // The slot a cell painted with value draws: value itself, or for a rule entry or one of its looks, the look its
    // neighbours pick now.
    internal int Pick(CellBlocks cells, int x, int y, int value) => _rules[value] is { } rules ? Pick(cells, x, y, rules) : value;

    // Whether a cell holding value, beside a paint on the given side of it, reads the painted cell.
    internal bool Reads(int value, int side) => _rules[value] is { } rules && (rules.Reads & (1 << Opposite(side))) != 0;

    // A fixed mix of a cell's position, the same on every machine and run.
    private static uint Mix(int x, int y)
    {
        uint hash = unchecked(((uint)x * 0x9E3779B1u) + ((uint)y * 0x85EBCA77u));
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x2C1B3C6Du);
        hash ^= hash >> 12;
        hash = unchecked(hash * 0x297A2D39u);

        return hash ^ (hash >> 15);
    }

    private static int Opposite(int side) => side < 4 ? (side + 2) & 3 : 4 + ((side - 2) & 3);

    // The sides a set joins, without a corner whose two sides do not both join.
    private static int Reduce(int joined)
    {
        foreach ((int corner, int sides) in Corners)
        {
            if ((joined & sides) != sides)
            {
                joined &= ~corner;
            }
        }

        return joined;
    }

    private static Rules RulesOf(TileType tileType, int index, TileRule[] authored, Dictionary<string, int> terrains, int first)
    {
        if (tileType.Cell is not null || tileType.Frames is not null)
        {
            throw Malformed(
                $"{Palette(tileType, index)} sets both {(tileType.Cell is null ? "frames" : "cell")} and rules. Keep rules for a tile that picks its look, or drop them.");
        }

        if (authored.Length == 0)
        {
            throw Malformed($"{Palette(tileType, index)} has no rules. Give it at least one rule, or drop rules.");
        }

        int reads = 0;
        int[] counts = new int[256];
        for (int rule = 0; rule < authored.Length; rule++)
        {
            if (authored[rule] is not { } look)
            {
                throw Malformed($"{Palette(tileType, index)}.rules[{rule}] is null. Give every rule a cell.");
            }

            int joined = (int)look.Joined;
            if (look.Cell < 0)
            {
                throw Malformed($"{Palette(tileType, index)}.rules[{rule}] draws cell {look.Cell}. Count cells from 0.");
            }

            if (Reduce(joined) != joined)
            {
                throw Malformed(
                    $"{Palette(tileType, index)}.rules[{rule}] joins \"{Names(joined)}\", naming a corner without both its sides. A corner joins only where both its sides do; add them, or drop the corner.");
            }

            reads |= joined;
            counts[joined]++;
        }

        // Rules that read no side pick by position alone, and join nothing.
        int terrain = 0;
        if (reads != 0 && (tileType.Joins ?? tileType.Terrain) is not { } joins)
        {
            throw Malformed($"{Palette(tileType, index)} has rules naming sides but neither joins nor terrain. Name the terrain its rules join.");
        }

        if ((tileType.Joins ?? tileType.Terrain) is { } named && !terrains.TryGetValue(Named(named, tileType, index, "joins"), out terrain))
        {
            throw Malformed(
                $"{Palette(tileType, index)} joins \"{named}\", which no tile type's terrain is. Give the tile types it meets that terrain, or join one of: {string.Join(", ", terrains.Keys)}.");
        }

        // Every combination of the read sides that can join needs a look. Walking the read sides' subsets from the
        // whole set down visits each once.
        for (int joined = reads; ; joined = (joined - 1) & reads)
        {
            if (Reduce(joined) == joined && counts[joined] == 0)
            {
                throw Malformed(
                    $"{Palette(tileType, index)} has no rule for {(joined == 0 ? "a cell nothing joins" : $"\"{Names(joined)}\" joined")}. Add one; its rules read \"{Names(reads)}\".");
            }

            if (joined == 0)
            {
                break;
            }
        }

        // Looks ordered by what joins them, each combination's looks a run starting where Starts says.
        ushort[] starts = new ushort[257];
        for (int joined = 0; joined < 256; joined++)
        {
            starts[joined + 1] = (ushort)(starts[joined] + counts[joined]);
        }

        ushort[] looks = new ushort[authored.Length];
        int[] filled = new int[256];
        for (int rule = 0; rule < authored.Length; rule++)
        {
            int joined = (int)authored[rule].Joined;
            looks[starts[joined] + filled[joined]++] = (ushort)(first + rule);
        }

        return new Rules(terrain, (byte)reads, starts, looks);
    }

    private static string Named(string name, TileType tileType, int index, string member) =>
        string.IsNullOrWhiteSpace(name) ? throw Malformed($"{Palette(tileType, index)} has a blank {member}. Name it, or drop it.") : name;

    // Sides as a document authors them, "up, right".
    private static string Names(int sides) => string.Join(
        ", ",
        Enum.GetValues<TileSides>().Where(side => side != TileSides.None && (sides & (int)side) != 0)
            .Select(static side => string.Concat(char.ToLowerInvariant(side.ToString()[0]).ToString(), side.ToString()[1..])));

    private static ArgumentException Malformed(string message) => TileGridValidation.Malformed(message, "tileTypes");

    private int Pick(CellBlocks cells, int x, int y, Rules rules)
    {
        int joined = 0;
        for (int side = 0; side < Offsets.Length; side++)
        {
            if ((rules.Reads & (1 << side)) == 0)
            {
                continue;
            }

            (int nx, int ny) = Beside(x, y, side);
            if ((uint)nx >= (uint)cells.Width || (uint)ny >= (uint)cells.Height || _terrains[cells.ValueAt(nx, ny)] == rules.Terrain)
            {
                joined |= 1 << side;
            }
        }

        joined = Reduce(joined);
        int start = rules.Starts[joined];
        int count = rules.Starts[joined + 1] - start;

        return rules.Looks[count == 1 ? start : start + (int)(Mix(x, y) % (uint)count)];
    }

    // One rule entry's looks: the terrain they join, the sides they read, and their slots by what joins them.
    private sealed record Rules(int Terrain, byte Reads, ushort[] Starts, ushort[] Looks);
}
