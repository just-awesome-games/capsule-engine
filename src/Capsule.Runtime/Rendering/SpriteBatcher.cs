using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;

namespace Capsule.Runtime.Rendering;

// Draws quads in submission order through one vertex buffer, matching SpriteBatch's output and
// beating its speed. A sprite is written straight into a staging array as four vertices, a chunk of
// staged sprites is uploaded once, and each run on a single texture goes to the device as one indexed
// draw. Every buffer is allocated in the constructor, and a frame at steady state allocates nothing.
internal sealed class SpriteBatcher : IDisposable
{
    // A chunk is one draw's worth of sprites. Four vertices each stay under the 16-bit index ceiling.
    private const int ChunkSprites = 8192;

    private const int ChunkVertices = ChunkSprites * 4;

    private const int IndicesPerSprite = 6;

    // The vertex buffer is a ring of chunks appended with NoOverwrite, and a flush never writes over
    // vertices an unfinished draw may still be reading. At the end of the ring the cursor wraps and
    // the write is Discard, which on GL orphans the store instead of stalling behind those draws.
    private const int RingChunks = 4;

    private const int RingVertices = ChunkVertices * RingChunks;

    private static readonly int Stride = SpriteVertex.VertexDeclaration.VertexStride;

    private readonly GraphicsDevice _device;
    private readonly SpriteEffect _effect;
    private readonly EffectPass _pass;
    private readonly DynamicVertexBuffer _vertices;
    private readonly IndexBuffer _indices;
    private readonly SpriteVertex[] _staging = new SpriteVertex[ChunkVertices];

    // The texture runs inside the staged chunk, in order. A chunk of sprites on alternating textures
    // costs one upload and a draw per run, where an upload per run would stall the driver behind the
    // previous draw.
    private readonly TextureRun[] _runs = new TextureRun[ChunkSprites];
    private int _runCount;

    // Vertices of the ring already handed to draws since the last wrap.
    private int _cursor;

    // Sprites staged and not yet flushed.
    private int _count;

    // The texture the open run draws with, and its texel size. SpriteBatch reads the same float from
    // the texture's internal TexelWidth, cached here once per run.
    private Texture2D? _texture;
    private float _texelWidth;
    private float _texelHeight;

    // The last colour converted and its packed form. Premultiplying costs three divisions, and a run
    // sharing one tint converts once.
    private ColorRgba _lastColor;
    private Color _lastPacked;

    internal SpriteBatcher(GraphicsDevice device)
    {
        _device = device;
        _effect = new SpriteEffect(device);
        _pass = _effect.CurrentTechnique.Passes[0];
        _vertices = new DynamicVertexBuffer(device, SpriteVertex.VertexDeclaration, RingVertices, BufferUsage.WriteOnly);
        _indices = new IndexBuffer(device, IndexElementSize.SixteenBits, ChunkSprites * IndicesPerSprite, BufferUsage.WriteOnly);
        _indices.SetData(QuadIndices());
    }

    // Two triangles per quad over vertices TL, TR, BL, BR, split as SpriteBatch splits them. The
    // diagonal runs TR to BL, and the other split rasterises the diagonal's pixels differently.
    private static ushort[] QuadIndices()
    {
        ushort[] indices = new ushort[ChunkSprites * IndicesPerSprite];
        for (int sprite = 0; sprite < ChunkSprites; sprite++)
        {
            int vertex = sprite * 4;
            int index = sprite * IndicesPerSprite;
            indices[index] = (ushort)vertex;
            indices[index + 1] = (ushort)(vertex + 1);
            indices[index + 2] = (ushort)(vertex + 2);
            indices[index + 3] = (ushort)(vertex + 1);
            indices[index + 4] = (ushort)(vertex + 3);
            indices[index + 5] = (ushort)(vertex + 2);
        }

        return indices;
    }

    // Opens a batch. Sets the device states, using SpriteBatch's defaults where none is given, and
    // applies the effect once. A caller-supplied effect owns its own parameters, while the batcher's
    // SpriteEffect maps transform then the viewport's projection.
    internal void Begin(
        in Matrix transform,
        SamplerState sampler,
        BlendState? blend = null,
        DepthStencilState? depth = null,
        RasterizerState? rasterizer = null,
        Effect? effect = null)
    {
        _device.BlendState = blend ?? BlendState.AlphaBlend;
        _device.DepthStencilState = depth ?? DepthStencilState.None;
        _device.RasterizerState = rasterizer ?? RasterizerState.CullCounterClockwise;
        _device.SamplerStates[0] = sampler;

        if (effect is null)
        {
            _effect.TransformMatrix = transform;
            _pass.Apply();
        }
        else
        {
            effect.CurrentTechnique.Passes[0].Apply();
        }

        _texture = null;
    }

    // A region of texture, moved by sliceOffset onto the page it is resident on. The remaining
    // parameters are SpriteQuad.Place's.
    internal void Draw(
        Texture2D texture,
        Vector2 position,
        Vector2 origin,
        Vector2 scale,
        in TextureRegion region,
        int sliceOffsetX,
        int sliceOffsetY,
        float rotation,
        bool flipX,
        bool flipY,
        ColorRgba color)
    {
        OpenRun(texture);

        SpriteQuad quad = SpriteQuad.Place(
            position,
            origin,
            scale,
            region.X + sliceOffsetX,
            region.Y + sliceOffsetY,
            region.Width,
            region.Height,
            _texelWidth,
            _texelHeight,
            rotation,
            flipX,
            flipY);

        Stage(in quad, color);
    }

    // The entire texture, as SpriteBatch draws a null source rectangle.
    internal void DrawWhole(Texture2D texture, Vector2 position, Vector2 origin, Vector2 scale, float rotation, ColorRgba color)
    {
        OpenRun(texture);

        SpriteQuad quad = SpriteQuad.PlaceWhole(position, origin, scale, texture.Width, texture.Height, rotation);

        Stage(in quad, color);
    }

    internal void End() => Flush();

    // Flushes the chunk if this sprite would not fit, then opens the texture's run or keeps the open
    // one. A texture change, compared by reference, closes the run.
    private void OpenRun(Texture2D texture)
    {
        if (_count == ChunkSprites)
        {
            Flush();
        }

        if (!ReferenceEquals(texture, _texture))
        {
            _texture = texture;
            _texelWidth = 1f / texture.Width;
            _texelHeight = 1f / texture.Height;
            _runs[_runCount++] = new TextureRun(texture, _count);
        }
        else if (_runCount == 0)
        {
            _runs[_runCount++] = new TextureRun(texture, _count);
        }
    }

    // The quad's four corners, in the order the index pattern reads them: top left, top right, bottom
    // left, bottom right. Written field by field through one advancing unchecked reference. At 50 000
    // sprites an indexed write per vertex measures seven percent slower, a span five, and staging a
    // whole vertex at a time twenty.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Stage(in SpriteQuad quad, ColorRgba color)
    {
        Color packed = Pack(color);
        ref SpriteVertex vertex = ref MemoryMarshal.GetArrayDataReference(_staging);
        vertex = ref Unsafe.Add(ref vertex, _count * 4);

        vertex.Position = new Vector3(quad.TopLeft.X, quad.TopLeft.Y, 0f);
        vertex.Color = packed;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexTopLeft.X, quad.TexTopLeft.Y);

        vertex = ref Unsafe.Add(ref vertex, 1);
        vertex.Position = new Vector3(quad.TopRight.X, quad.TopRight.Y, 0f);
        vertex.Color = packed;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexBottomRight.X, quad.TexTopLeft.Y);

        vertex = ref Unsafe.Add(ref vertex, 1);
        vertex.Position = new Vector3(quad.BottomLeft.X, quad.BottomLeft.Y, 0f);
        vertex.Color = packed;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexTopLeft.X, quad.TexBottomRight.Y);

        vertex = ref Unsafe.Add(ref vertex, 1);
        vertex.Position = new Vector3(quad.BottomRight.X, quad.BottomRight.Y, 0f);
        vertex.Color = packed;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexBottomRight.X, quad.TexBottomRight.Y);

        _count++;
    }

    // ColorRgba is straight alpha and the backend blend convention is premultiplied.
    private Color Pack(ColorRgba color)
    {
        if (color != _lastColor)
        {
            _lastColor = color;
            _lastPacked = Color.FromNonPremultiplied(color.R, color.G, color.B, color.A);
        }

        return _lastPacked;
    }

    private void Flush()
    {
        if (_count == 0)
        {
            return;
        }

        int vertices = _count * 4;
        SetDataOptions options = SetDataOptions.NoOverwrite;
        if (_cursor + vertices > RingVertices)
        {
            _cursor = 0;
            options = SetDataOptions.Discard;
        }

        _vertices.SetData(_cursor * Stride, _staging, 0, vertices, Stride, options);

        _device.SetVertexBuffer(_vertices);
        _device.Indices = _indices;

        // The index pattern starts at vertex zero for every quad, and a run starting part-way into
        // the chunk draws from its first vertex as the base.
        for (int i = 0; i < _runCount; i++)
        {
            TextureRun run = _runs[i];
            int last = i + 1 < _runCount ? _runs[i + 1].First : _count;

            _device.Textures[0] = run.Texture;
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, _cursor + (run.First * 4), 0, (last - run.First) * 2);
        }

        _cursor += vertices;
        _count = 0;
        _runCount = 0;
    }

    // First is the index of the run's first sprite in the staged chunk. The run ends where the next
    // run starts, or at the chunk's count.
    private readonly record struct TextureRun(Texture2D Texture, int First);

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
        _effect.Dispose();
    }
}
