using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;

namespace Capsule.Runtime.Rendering;

// Draws quads in submission order through one vertex buffer, matching SpriteBatch's output and
// beating its speed. A sprite is written straight into a staging array as four vertices, a chunk of
// staged sprites is uploaded once, and each run on a single texture and material goes to the device as
// one indexed draw. Every buffer is allocated in the constructor, and a frame at steady state allocates
// nothing.
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
    private readonly EffectStore _effects;
    private readonly DynamicVertexBuffer _vertices;
    private readonly IndexBuffer _indices;
    private readonly SpriteVertex[] _staging = new SpriteVertex[ChunkVertices];

    // The runs inside the staged chunk, in order. A chunk of sprites on alternating textures costs one
    // upload and a draw per run, where an upload per run would stall the driver behind the previous
    // draw.
    private readonly SpriteRun[] _runs = new SpriteRun[ChunkSprites];
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

    // The material the next sprite draws with. Null is Capsule's own sprite shader.
    private Material? _material;

    // The material whose effect the device has applied since Begin, and the transform every effect
    // applied in this batch maps by: the caller's transform, then the viewport's projection.
    private Material? _applied;
    private Matrix _transform;

    // The sampler the batch was opened with, which a material's textures sample through too.
    private SamplerState _sampler = SamplerState.LinearClamp;

    // The last colour and blend converted and their packed form. Premultiplying costs three divisions,
    // and a run sharing one tint and blend converts once.
    private ColorRgba _lastColor;
    private BlendMode _lastBlend;
    private Color _lastPacked;

    // The same cache for the flash, keyed by the flash and the tint alpha that scales it.
    private ColorRgba _lastFlash;
    private byte _lastFlashAlpha;
    private Color _lastPackedFlash;

    internal SpriteBatcher(GraphicsDevice device, EffectStore effects)
    {
        _device = device;
        _effects = effects;
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

    // Opens a batch drawing with Capsule's own sprite shader. Sets the device states, using
    // SpriteBatch's defaults where none is given, and applies the shader with transform then the
    // viewport's projection.
    internal void Begin(
        in Matrix transform,
        SamplerState sampler,
        BlendState? blend = null,
        DepthStencilState? depth = null,
        RasterizerState? rasterizer = null)
    {
        _device.BlendState = blend ?? BlendState.AlphaBlend;
        _device.DepthStencilState = depth ?? DepthStencilState.None;
        _device.RasterizerState = rasterizer ?? RasterizerState.CullCounterClockwise;
        _device.SamplerStates[0] = sampler;

        _transform = transform * Projection(_device);
        _effects.Apply(null, in _transform, sampler);
        _sampler = sampler;
        _applied = null;

        _texture = null;
        _material = null;
    }

    // The material the sprites drawn next use, null for Capsule's own sprite shader. Begin resets it to
    // null. A change closes the open run by forgetting its texture, so the sprite test stays the one
    // texture comparison.
    internal void SetMaterial(Material? material)
    {
        if (!ReferenceEquals(material, _material))
        {
            _material = material;
            _texture = null;
        }
    }

    // The viewport as clip space, as SpriteEffect maps it: an orthographic projection with Y down and
    // the origin at the viewport's top-left corner, moved half a pixel on a device that addresses
    // pixels by their corners.
    private static Matrix Projection(GraphicsDevice device)
    {
        Viewport viewport = device.Viewport;
        Matrix.CreateOrthographicOffCenter(0f, viewport.Width, viewport.Height, 0f, 0f, -1f, out Matrix projection);

        if (device.UseHalfPixelOffset)
        {
            projection.M41 += -0.5f * projection.M11;
            projection.M42 += -0.5f * projection.M22;
        }

        return projection;
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
        ColorRgba color,
        BlendMode blend,
        ColorRgba flash)
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

        Stage(in quad, color, blend, flash);
    }

    // The entire texture, as SpriteBatch draws a null source rectangle. Always alpha: nothing here draws
    // from a SpriteIntent, so nothing here carries a blend of its own.
    internal void DrawWhole(Texture2D texture, Vector2 position, Vector2 origin, Vector2 scale, float rotation, ColorRgba color)
    {
        OpenRun(texture);

        SpriteQuad quad = SpriteQuad.PlaceWhole(position, origin, scale, texture.Width, texture.Height, rotation);

        Stage(in quad, color, BlendMode.Alpha, default);
    }

    internal void End() => Flush();

    // Flushes the chunk if this sprite would not fit, then opens a run or keeps the open one. A
    // texture change, compared by reference, closes the run, and so does a material change.
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
            _runs[_runCount++] = new SpriteRun(texture, _material, _count);
        }
        else if (_runCount == 0)
        {
            _runs[_runCount++] = new SpriteRun(texture, _material, _count);
        }
    }

    // The quad's four corners, in the order the index pattern reads them: top left, top right, bottom
    // left, bottom right. Written field by field through one advancing unchecked reference. At 50 000
    // sprites an indexed write per vertex measures seven percent slower, a span five, and staging a
    // whole vertex at a time twenty.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Stage(in SpriteQuad quad, ColorRgba color, BlendMode blend, ColorRgba flash)
    {
        // The tint and the flash sit side by side in the vertex and are written as one eight-byte store.
        // A sprite that is not flashing skips the flash's conversion.
        ulong colors = Pack(color, blend).PackedValue
            | (flash.A == 0 ? 0UL : (ulong)PackFlash(flash, color.A).PackedValue << 32);
        ref SpriteVertex vertex = ref MemoryMarshal.GetArrayDataReference(_staging);
        vertex = ref Unsafe.Add(ref vertex, _count * 4);

        vertex.Position = new Microsoft.Xna.Framework.Vector2(quad.TopLeft.X, quad.TopLeft.Y);
        Unsafe.As<Color, ulong>(ref vertex.Color) = colors;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexTopLeft.X, quad.TexTopLeft.Y);

        vertex = ref Unsafe.Add(ref vertex, 1);
        vertex.Position = new Microsoft.Xna.Framework.Vector2(quad.TopRight.X, quad.TopRight.Y);
        Unsafe.As<Color, ulong>(ref vertex.Color) = colors;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexBottomRight.X, quad.TexTopLeft.Y);

        vertex = ref Unsafe.Add(ref vertex, 1);
        vertex.Position = new Microsoft.Xna.Framework.Vector2(quad.BottomLeft.X, quad.BottomLeft.Y);
        Unsafe.As<Color, ulong>(ref vertex.Color) = colors;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexTopLeft.X, quad.TexBottomRight.Y);

        vertex = ref Unsafe.Add(ref vertex, 1);
        vertex.Position = new Microsoft.Xna.Framework.Vector2(quad.BottomRight.X, quad.BottomRight.Y);
        Unsafe.As<Color, ulong>(ref vertex.Color) = colors;
        vertex.TextureCoordinate = new Microsoft.Xna.Framework.Vector2(quad.TexBottomRight.X, quad.TexBottomRight.Y);

        _count++;
    }

    // ColorRgba is straight alpha and the backend blend convention is premultiplied, so an alpha
    // intent is premultiplied here. Additive draws with no state change against the same premultiplied
    // pipeline: an alpha of zero contributes nothing to cover and the colour, already scaled by its
    // alpha, adds as src.rgb + dst.rgb x (1 - 0).
    private Color Pack(ColorRgba color, BlendMode blend)
    {
        if (color != _lastColor || blend != _lastBlend)
        {
            _lastColor = color;
            _lastBlend = blend;

            (byte r, byte g, byte b, byte a) = PackInput(color, blend);
            _lastPacked = blend == BlendMode.Additive
                ? new Color(r, g, b, a)
                : Color.FromNonPremultiplied(r, g, b, a);
        }

        return _lastPacked;
    }

    // The bytes Pack hands to the backend's colour constructor, with no MonoGame type so a test can
    // assert on it. Additive hands the colour scaled by its alpha and zero alpha, so a fading glow fades
    // with no state change. Alpha hands the intent's colour and alpha through exactly, for
    // Color.FromNonPremultiplied to premultiply as it always has (D-capsule-109).
    internal static (byte R, byte G, byte B, byte A) PackInput(ColorRgba color, BlendMode blend) =>
        blend == BlendMode.Additive
            ? (ColorRgba.Multiply(color.R, color.A), ColorRgba.Multiply(color.G, color.A), ColorRgba.Multiply(color.B, color.A), (byte)0)
            : (color.R, color.G, color.B, color.A);

    private Color PackFlash(ColorRgba flash, byte tintAlpha)
    {
        if (flash != _lastFlash || tintAlpha != _lastFlashAlpha)
        {
            _lastFlash = flash;
            _lastFlashAlpha = tintAlpha;

            (byte r, byte g, byte b, byte a) = PackFlashInput(flash, tintAlpha);
            _lastPackedFlash = new Color(r, g, b, a);
        }

        return _lastPackedFlash;
    }

    // The bytes PackFlash hands to the backend's colour constructor. The flash colour is scaled by the
    // sprite's straight tint alpha, so a flash fades as the sprite does under either blend, and the
    // amount passes through. A zero amount leaves the shader's colour exactly as it was.
    internal static (byte R, byte G, byte B, byte A) PackFlashInput(ColorRgba flash, byte tintAlpha) =>
        (ColorRgba.Multiply(flash.R, tintAlpha), ColorRgba.Multiply(flash.G, tintAlpha), ColorRgba.Multiply(flash.B, tintAlpha), flash.A);

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
        // the chunk draws from its first vertex as the base. An effect is applied only where the
        // material changes, and applying one rebinds slot 0, so the run's texture is bound after.
        for (int i = 0; i < _runCount; i++)
        {
            SpriteRun run = _runs[i];
            int last = i + 1 < _runCount ? _runs[i + 1].First : _count;

            if (!ReferenceEquals(run.Material, _applied))
            {
                _effects.Apply(run.Material, in _transform, _sampler);
                _applied = run.Material;
            }

            _device.Textures[0] = run.Texture;
            _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, _cursor + (run.First * 4), 0, (last - run.First) * 2);
        }

        _cursor += vertices;
        _count = 0;
        _runCount = 0;
    }

    // First is the index of the run's first sprite in the staged chunk. The run ends where the next
    // run starts, or at the chunk's count.
    private readonly record struct SpriteRun(Texture2D Texture, Material? Material, int First);

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}
