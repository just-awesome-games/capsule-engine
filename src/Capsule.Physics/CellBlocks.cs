using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Capsule.Physics;

// A grid's cells, each a value and a variant, kept in square blocks of BlockSize cells a side. A block
// exists only while a cell in it holds a nonzero value or variant, and every cell outside a block holds
// value 0 with variant 0. A block holds its variants only once one of them is nonzero.
// A copy made by Share reads its source's blocks until it writes one, and the source is never written.
internal sealed class CellBlocks
{
    internal const int BlockShift = 4;
    internal const int BlockSize = 1 << BlockShift;
    internal const int BlockArea = BlockSize * BlockSize;

    // The largest value a cell holds.
    internal const int MaxValue = ushort.MaxValue;

    private const int Mask = BlockSize - 1;

    // Row-major, BlocksWide per row, null where a block holds nothing.
    private readonly Block?[] _blocks;

    // The storage this one was shared from, whose blocks it reads until it writes them, or null.
    private readonly CellBlocks? _source;

    // Cells across.
    internal int Width { get; }

    // Cells down.
    internal int Height { get; }

    internal int BlocksWide { get; }

    internal int BlocksHigh { get; }

    internal CellBlocks(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        BlocksWide = ((width - 1) >> BlockShift) + 1;
        BlocksHigh = ((height - 1) >> BlockShift) + 1;
        _blocks = new Block?[BlocksWide * BlocksHigh];
    }

    private CellBlocks(CellBlocks source)
    {
        Width = source.Width;
        Height = source.Height;
        BlocksWide = source.BlocksWide;
        BlocksHigh = source.BlocksHigh;
        _blocks = [.. source._blocks];
        _source = source;
    }

    // How many blocks hold a cell.
    internal int AllocatedBlocks
    {
        get
        {
            int count = 0;
            foreach (Block? block in _blocks)
            {
                count += block is null ? 0 : 1;
            }

            return count;
        }
    }

    // Cells from row-major values and variants, Width * Height of each. An empty variants span means every
    // variant is 0.
    internal static CellBlocks FromDense(int width, int height, ReadOnlySpan<int> values, ReadOnlySpan<byte> variants)
    {
        CellBlocks cells = new(width, height);
        long area = (long)width * height;
        if (values.Length != area)
        {
            throw new ArgumentException($"values has {values.Length} entries but {width}x{height} requires {area}.", nameof(values));
        }

        if (!variants.IsEmpty && variants.Length != area)
        {
            throw new ArgumentException($"variants has {variants.Length} entries but {width}x{height} requires {area}.", nameof(variants));
        }

        // Each row skips its runs of empty cells a vector at a time. A turned cell with value 0 is the rare
        // case, written in a second pass.
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int skip = values.Slice(row + x, width - x).IndexOfAnyExcept(0);
                if (skip < 0)
                {
                    break;
                }

                x += skip;
                int value = values[row + x];
                if ((uint)value > MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(values), value, $"values[{row + x}] is {value}. Keep every value in 0..{MaxValue}.");
                }

                cells.Set(x, y, value, variants.IsEmpty ? 0 : variants[row + x]);
            }
        }

        for (int index = variants.IsEmpty ? -1 : variants.IndexOfAnyExcept((byte)0); index >= 0;)
        {
            if (values[index] == 0)
            {
                cells.Set(index % width, index / width, 0, variants[index]);
            }

            int next = variants[(index + 1)..].IndexOfAnyExcept((byte)0);
            index = next < 0 ? -1 : index + 1 + next;
        }

        return cells;
    }

    // The cell's position within its block.
    internal static int CellOf(int x, int y) => ((y & Mask) << BlockShift) | (x & Mask);

    // A copy that reads these cells until it writes one. This storage is never written through it.
    internal CellBlocks Share() => new(this);

    // A new empty block at block column bx and block row by, where none is yet, which the caller fills.
    internal Block Add(int bx, int by) => _blocks[(by * BlocksWide) + bx] = new Block();

    // The block holding the cells of block column bx and block row by, or null where they hold nothing.
    internal Block? BlockAt(int bx, int by) => _blocks[(by * BlocksWide) + bx];

    // The caller checks the cell lies on the grid.
    internal int ValueAt(int x, int y) => _blocks[BlockIndex(x, y)] is { } block ? block.Values[CellOf(x, y)] : 0;

    internal int VariantAt(int x, int y) => _blocks[BlockIndex(x, y)] is { } block ? block.VariantAt(CellOf(x, y)) : 0;

    // The cell's value * stride + its variant, read with one block lookup.
    internal int KeyAt(int x, int y, int stride)
    {
        if (_blocks[BlockIndex(x, y)] is not { } block)
        {
            return 0;
        }

        int cell = CellOf(x, y);

        return (block.Values[cell] * stride) + block.VariantAt(cell);
    }

    // Finds the first cell, block by block, whose value is values or more or whose variant is variants or more.
    internal bool TryFindBeyond(int values, int variants, out int x, out int y)
    {
        for (int index = 0; index < _blocks.Length; index++)
        {
            if (_blocks[index] is not { } block)
            {
                continue;
            }

            for (int cell = 0; cell < BlockArea; cell++)
            {
                if (block.Values[cell] >= values || block.VariantAt(cell) >= variants)
                {
                    x = ((index % BlocksWide) << BlockShift) + (cell & Mask);
                    y = ((index / BlocksWide) << BlockShift) + (cell >> BlockShift);
                    return true;
                }
            }
        }

        (x, y) = (0, 0);
        return false;
    }

    internal void RequireOnGrid(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }

    // Writes a cell the caller checked lies on the grid, with a value in 0..MaxValue. A block is allocated on
    // its first nonzero cell and released when its last one clears.
    internal void Set(int x, int y, int value, int variant)
    {
        int index = BlockIndex(x, y);
        Block? block = _blocks[index];
        if (block is null)
        {
            if (value == 0 && variant == 0)
            {
                return;
            }

            block = _blocks[index] = new Block();
        }
        else if (_source is not null && ReferenceEquals(block, _source._blocks[index]))
        {
            block = _blocks[index] = block.Clone();
        }

        if (block.Write(CellOf(x, y), (ushort)value, (byte)variant) == 0)
        {
            _blocks[index] = null;
        }
    }

    private int BlockIndex(int x, int y) => ((y >> BlockShift) * BlocksWide) + (x >> BlockShift);

    // One block's cells, row-major within the block.
    internal sealed class Block
    {
        internal Cells Values;

        // Allocated with the first nonzero variant.
        private byte[]? _variants;

        // How many cells hold a nonzero value or variant.
        private int _painted;

        // Whether the block has allocated its variants.
        internal bool HoldsVariants => _variants is not null;

        internal int VariantAt(int cell) => _variants is { } variants ? variants[cell] : 0;

        internal Block Clone() => new() { Values = Values, _variants = _variants is { } variants ? [.. variants] : null, _painted = _painted };

        // Fills a block Add returned from BlockArea little-endian ushort values and, unless empty, BlockArea variants.
        // A bulk fill copies the values' bytes as they lie, which reads them right only on a little-endian machine.
        internal void Fill(ReadOnlySpan<byte> values, ReadOnlySpan<byte> variants, bool bulk)
        {
            Span<ushort> cells = Values;
            if (bulk)
            {
                values[..(BlockArea * sizeof(ushort))].CopyTo(MemoryMarshal.AsBytes(cells));
            }
            else
            {
                for (int cell = 0; cell < BlockArea; cell++)
                {
                    cells[cell] = BinaryPrimitives.ReadUInt16LittleEndian(values[(cell * sizeof(ushort))..]);
                }
            }

            if (!variants.IsEmpty)
            {
                _variants = variants.ToArray();
            }

            _painted = 0;
            for (int cell = 0; cell < BlockArea; cell++)
            {
                _painted += cells[cell] != 0 || VariantAt(cell) != 0 ? 1 : 0;
            }
        }

        // Returns how many cells still hold something.
        internal int Write(int cell, ushort value, byte variant)
        {
            bool was = Values[cell] != 0 || VariantAt(cell) != 0;
            Values[cell] = value;
            if (variant != 0 || _variants is not null)
            {
                _variants ??= new byte[BlockArea];
                _variants[cell] = variant;
            }

            _painted += (value != 0 || variant != 0 ? 1 : 0) - (was ? 1 : 0);

            return _painted;
        }
    }

    [InlineArray(BlockArea)]
    internal struct Cells
    {
        private ushort _element;
    }
}
