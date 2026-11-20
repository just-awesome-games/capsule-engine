using System.Runtime.InteropServices;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// The chunks of tiles a presenting host keeps on the device, each in a slot of a shared page of instances and drawn
// as one draw. A chunk is written again only where a stamp its run names, the style it was written under or the
// texture slice its instances bake in has changed since. The chunks new to a frame share one page, created at that
// frame's upload, since creating a device buffer costs far more than writing a chunk. Past the cap, the chunk drawn
// longest ago gives its slot to the next new one, and a view that keeps scrolling allocates nothing once the cap is
// reached. A chunk no frame has drawn for Unused frames gives up its slot at the next sweep too, and lets go of its
// source. A frame writes at most NewPerFrame chunks it does not hold yet, and draws the cells it shows of any other
// as a frame that expands its tiles would, which spreads a scene's first chunks over its first frames.
internal sealed class TileChunks : IDisposable
{
    // Whether every chunk drawn as it was kept is written again and compared with what it kept, throwing where the
    // two differ. A windowed run with it on checks every chunk on every frame.
    internal static readonly bool ChecksSwitch = AppContext.TryGetSwitch("Capsule.CheckTiles", out bool on) && on;

    // The fewest chunks kept before the one drawn longest ago is given up, whatever the frames draw.
    private const int LeastKept = 64;

    // The fewest slots a page is created with. A page created for fewer chunks lends the rest to later frames. The
    // first page holds LeastKept, and a view scrolling below the cap creates no buffer after the first frame.
    private const int LeastSlots = 16;

    // How many frames a chunk is kept undrawn, and how often the chunks are swept for those past it.
    private const int Unused = 1024;

    // How many chunks a frame writes that it does not hold yet, which keeps each of a scene's first frames as cheap as
    // one that expands every tile.
    private const int NewPerFrame = 8;

    private readonly Dictionary<Key, Chunk> _chunks = [];

    // Whether each chunk drawn as kept is compared with a fresh write, which makes every chunk keep its instances.
    private readonly bool _checks;

    // The sprites of the chunk being written, and the instances of every chunk written since the last upload, back
    // to back in the order of _pending.
    private readonly SpriteIntent[] _sprites = new SpriteIntent[TileRun.ChunkArea];
    private SpriteInstance[] _written = new SpriteInstance[TileRun.ChunkArea];
    private int _writtenCount;
    private readonly List<Chunk> _pending = [];

    // Every page, the chunks on them that name no source, and the page the next upload creates, which takes a slot for
    // each chunk that finds none free until then.
    private readonly List<Page> _pages = [];
    private readonly Stack<Chunk> _free = [];
    private Page? _opening;

    // How many frames have begun, how many chunks the current one has drawn and taken new, and the most any one has
    // drawn.
    private int _frame;
    private int _drawn;
    private int _taken;
    private int _mostDrawn;

    internal TileChunks(bool checks) => _checks = checks;

    // How many chunks are kept.
    internal int Count => _chunks.Count;

    // Begins a frame. No chunk a frame draws gives up its slot before the next one begins. Writes no upload took,
    // which only a frame with no device leaves, are dropped.
    internal void BeginFrame()
    {
        _frame++;
        _drawn = 0;
        _taken = 0;
        _pending.Clear();
        _writtenCount = 0;
        if (_frame % Unused == 0)
        {
            Release(Unused);
        }
    }

    // Gives up every chunk and lets go of every source, as a scene boundary does.
    internal void Forget() => Release(0);

    // The chunk run names, written first where it is new or stale, or null for a chunk not held yet once the frame
    // has taken NewPerFrame. tint and flash style every tile as the frame would have, and slice is where the
    // source's texture lies.
    internal Chunk? Resolve(in TileRun run, ColorRgba tint, ColorRgba flash, in TextureSlice slice)
    {
        Key key = new(run.Source, run.X, run.Y);
        if (!_chunks.TryGetValue(key, out Chunk? chunk))
        {
            if (_taken == NewPerFrame)
            {
                return null;
            }

            _taken++;
            chunk = Recycle() ?? Open();
            _chunks.Add(key, chunk);
            chunk.Key = key;
            chunk.Texture = null;
        }

        if (chunk.Frame != _frame)
        {
            chunk.Frame = _frame;
            _mostDrawn = Math.Max(_mostDrawn, ++_drawn);
        }

        if (chunk.Holds(in run, tint, flash, in slice))
        {
            if (_checks)
            {
                Check(chunk, in run, tint, flash, in slice);
            }

            return chunk;
        }

        int first = _writtenCount;
        chunk.Count = Write(in run, tint, flash, in slice, out chunk.Animated);
        chunk.Stamp(in run, tint, flash, in slice);
        if (_checks)
        {
            chunk.Kept = _written.AsSpan(first, chunk.Count).ToArray();
        }

        if (chunk.Count > 0)
        {
            chunk.Written = first;
            _pending.Add(chunk);
        }

        return chunk;
    }

    // Creates the page chunks new since the last upload took slots on, and uploads every chunk written since then.
    // Returns the bytes it uploaded.
    internal int Upload(GpuDevice device, GpuCopyPass copy)
    {
        if (_opening is { } opening)
        {
            int slots = Math.Max(_pages.Count == 0 ? LeastKept : LeastSlots, opening.Slots);
            for (int slot = slots - 1; slot >= opening.Slots; slot--)
            {
                _free.Push(new Chunk(opening, slot));
            }

            opening.Slots = slots;
            _pages.Add(opening);
            _opening = null;
        }

        int bytes = 0;
        foreach (Chunk chunk in _pending)
        {
            bytes += chunk.Page.Buffer.Write(device, copy, chunk.First, _written.AsSpan(chunk.Written, chunk.Count), chunk.Page.Slots * TileRun.ChunkArea);
        }

        _pending.Clear();
        _writtenCount = 0;

        return bytes;
    }

    public void Dispose()
    {
        foreach (Page page in _pages)
        {
            page.Buffer.Dispose();
        }

        _pages.Clear();
        _chunks.Clear();
    }

    // The sprites of the cells the frame shows of the run's chunk, styled, as a frame that expands its tiles writes
    // them. Invalidated by the next write.
    internal ReadOnlySpan<SpriteIntent> WriteShown(in TileRun run, ColorRgba tint, ColorRgba flash) =>
        Styled(run.Source.Write(run.MinX, run.MinY, run.MaxX, run.MaxY, _sprites, out _), tint, flash);

    // Writes the run's chunk after the instances written so far, and returns how many it wrote.
    private int Write(in TileRun run, ColorRgba tint, ColorRgba flash, in TextureSlice slice, out bool animated)
    {
        int x = run.X << TileRun.ChunkShift;
        int y = run.Y << TileRun.ChunkShift;
        Span<SpriteIntent> sprites = Styled(run.Source.Write(x, y, x + TileRun.ChunkSize, y + TileRun.ChunkSize, _sprites, out animated), tint, flash);
        int count = sprites.Length;

        if (_writtenCount + count > _written.Length)
        {
            Array.Resize(ref _written, Math.Max(_written.Length * 2, _writtenCount + count));
        }

        SpriteDrawList.Write(sprites, _written.AsSpan(_writtenCount, count), slice.OffsetX, slice.OffsetY);
        _writtenCount += count;

        return count;
    }

    // The first count written sprites, styled by tint and flash.
    private Span<SpriteIntent> Styled(int count, ColorRgba tint, ColorRgba flash)
    {
        Span<SpriteIntent> sprites = _sprites.AsSpan(0, count);
        if (tint != ColorRgba.White || flash.A != 0)
        {
            foreach (ref SpriteIntent sprite in sprites)
            {
                sprite = FrameView.Styled(in sprite, tint, flash);
            }
        }

        return sprites;
    }

    // Writes a chunk drawn as kept again, and throws where its instances differ from the kept ones.
    private void Check(Chunk chunk, in TileRun run, ColorRgba tint, ColorRgba flash, in TextureSlice slice)
    {
        int first = _writtenCount;
        int count = Write(in run, tint, flash, in slice, out _);
        ReadOnlySpan<SpriteInstance> written = _written.AsSpan(first, count);
        _writtenCount = first;
        if (!MemoryMarshal.AsBytes(written).SequenceEqual(MemoryMarshal.AsBytes(chunk.Kept.AsSpan())))
        {
            throw new InvalidOperationException(
                $"A kept chunk of tiles at ({run.X}, {run.Y}) holds {chunk.Kept!.Length} instances, and writing it again makes {count} that differ. A change to what the chunk draws moved no stamp its run names.");
        }
    }

    // A chunk on a free slot, or a new one on a slot of the page the next upload creates.
    private Chunk Open()
    {
        if (_free.TryPop(out Chunk? free))
        {
            return free;
        }

        Page opening = _opening ??= new Page();

        return new Chunk(opening, opening.Slots++);
    }

    // Frees the slot of every chunk no frame has drawn for age frames.
    private void Release(int age)
    {
        foreach (Chunk chunk in _chunks.Values)
        {
            if (_frame - chunk.Frame >= age)
            {
                _chunks.Remove(chunk.Key);
                chunk.Key = default;
                chunk.Texture = null;
                chunk.Kept = null;
                _free.Push(chunk);
            }
        }
    }

    // The chunk drawn longest ago, given up for another once the cap is reached, or null below it.
    private Chunk? Recycle()
    {
        if (_chunks.Count < Math.Max(LeastKept, 2 * _mostDrawn))
        {
            return null;
        }

        Chunk? oldest = null;
        foreach (Chunk chunk in _chunks.Values)
        {
            if (chunk.Frame != _frame && (oldest is null || chunk.Frame < oldest.Frame))
            {
                oldest = chunk;
            }
        }

        if (oldest is not null)
        {
            _chunks.Remove(oldest.Key);
        }

        return oldest;
    }

    // A chunk is named by its source and its place in the source's grid of chunks.
    internal readonly record struct Key(ITileSource Source, int X, int Y);

    // A device buffer of Slots chunks, ChunkArea instances each.
    internal sealed class Page
    {
        internal readonly InstanceBuffer Buffer = new();
        internal int Slots;
    }

    // One kept chunk: the page slot it holds and how many instances it holds there, and what they were written from.
    internal sealed class Chunk(Page page, int slot)
    {
        internal readonly Page Page = page;

        // The first of its instances on the page.
        internal readonly int First = slot * TileRun.ChunkArea;

        internal Key Key;
        internal int Count;

        // Where its instances wait among the written ones until the upload.
        internal int Written;

        // Whether any of its cells holds an animated entry, which makes it read its run's Frames.
        internal bool Animated;

        // Its instances, which only chunks that check keep.
        internal SpriteInstance[]? Kept;

        // The frame that last drew it.
        internal int Frame;

        // The texture page the instances lie on, null before the chunk is first written.
        internal SampledTexture? Texture;

        private int _cells;
        private int _frames;
        private ColorRgba _tint;
        private ColorRgba _flash;

        // Where the source's texture lies on the texture page.
        private int _offsetX;
        private int _offsetY;

        // Whether the chunk holds what run, styled by tint and flash, draws from slice.
        internal bool Holds(in TileRun run, ColorRgba tint, ColorRgba flash, in TextureSlice slice) =>
            ReferenceEquals(Texture, slice.Texture)
            && _cells == run.Cells
            && (!Animated || _frames == run.Frames)
            && _tint == tint
            && _flash == flash
            && _offsetX == slice.OffsetX
            && _offsetY == slice.OffsetY;

        internal void Stamp(in TileRun run, ColorRgba tint, ColorRgba flash, in TextureSlice slice)
        {
            _cells = run.Cells;
            _frames = run.Frames;
            _tint = tint;
            _flash = flash;
            Texture = slice.Texture;
            _offsetX = slice.OffsetX;
            _offsetY = slice.OffsetY;
        }
    }
}
