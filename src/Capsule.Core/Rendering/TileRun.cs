using Capsule.Assets;

namespace Capsule.Rendering;

// A grid of tiles a presenting host can keep on the device a chunk at a time. The view names the chunks a frame
// shows, and the host writes a chunk's sprites only when a stamp it names has changed since the host last wrote it.
internal interface ITileSource
{
    // The texture every tile is cut from.
    TextureHandle Texture { get; }

    // Writes the sprite of every drawn cell from minX, minY up to maxX, maxY, row by row from the front of
    // sprites, which holds the whole rect, and returns how many it wrote. animated is whether any of the cells
    // holds an entry whose frames change as the source steps. A frame that expands the tiles writes them the same.
    int Write(int minX, int minY, int maxX, int maxY, Span<SpriteIntent> sprites, out bool animated);
}

// One chunk of Source's cells a frame shows, ChunkSize cells a side from chunk column X and row Y, of which the
// frame reaches the cells from MinX, MinY up to MaxX, MaxY. Cells changes whenever any of its cells does, and Frames
// whenever an animated entry of the source shows another frame. A chunk holding no animated entry reads no Frames.
internal readonly record struct TileRun(ITileSource Source, int X, int Y, int Cells, int Frames, int MinX, int MinY, int MaxX, int MaxY)
{
    // A chunk is 32 cells a side, measured between two costs. Each chunk is one draw on every display frame, and 16
    // a side doubled a redrawn frame's cost. A chunk is written whole when the view first reaches it, and 64 a side
    // made those frames half again as long.
    internal const int ChunkShift = 5;
    internal const int ChunkSize = 1 << ChunkShift;
    internal const int ChunkArea = ChunkSize * ChunkSize;
}
