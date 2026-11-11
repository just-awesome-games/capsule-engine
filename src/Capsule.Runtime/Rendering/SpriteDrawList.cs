using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// A frame's quads, recorded as one sprite instance each in submission order, and the draws that cover
// them. Consecutive quads on one texture under the same state are one draw. Once recorded, the frame
// reaches the device as one upload and then its passes, each draw binding only the state that changed.
// The sprite vertex stage interpolates, snaps and places each instance. Every array grows to the
// largest frame and is kept, and a frame at steady state allocates nothing.
internal sealed class SpriteDrawList : IDisposable
{
    private readonly GpuDevice _device;
    private readonly ShaderStore _shaders;
    private readonly TextureSamplers _samplers;

    // How far past its sprite a read of the picture reaches before it repeats the copy's edge, in the sprite's
    // units. The copy stops there, and a small strip does not pay for the whole surface.
    private const float ReadReach = 64f;

    private SpriteInstance[] _instances = new SpriteInstance[1024];
    private int _count;
    private Draw[] _draws = new Draw[64];
    private int _drawCount;
    private Pass[] _passes = new Pass[8];
    private int _passCount;
    private GpuBuffer? _buffer;
    private float _alpha;

    // What the next quad draws under. A change closes the open draw.
    private State _state;
    private bool _open;

    internal SpriteDrawList(GpuDevice device, ShaderStore shaders, TextureSamplers samplers)
    {
        _device = device;
        _shaders = shaders;
        _samplers = samplers;
    }

    // Forgets the last recording. alpha is the fraction of a step every instance interpolates by.
    internal void Reset(float alpha)
    {
        _alpha = alpha;
        _count = 0;
        _drawCount = 0;
        _passCount = 0;
        _open = false;
    }

    // Draws what follows into target after clearing it to clear, a straight-alpha colour, unless that is
    // null. The pass starts over the whole target with no transform, point sampling, premultiplied
    // blending, Capsule's own shader, and no clip or placement.
    internal void BeginPass(GpuTexture target, ColorRgba? clear)
    {
        Grow(ref _passes, _passCount);
        _passes[_passCount++] = new Pass(target, clear, _drawCount);
        _state = new State
        {
            Viewport = new Area(0, 0, target.Width, target.Height),
            Transform = Matrix4x4.Identity,
            Sampler = _samplers.Get(TextureSampling.Point),
            Blend = ShaderStore.Premultiplied,
        };
        _open = false;
    }

    // Splits the pass here for a material that reads the picture: the target's pixels so far are copied to
    // copy, under the same place, before the quads drawn next, which draw on over them with every setting
    // kept. The copy covers what Widen adds.
    internal void BeginRead(SampledTexture copy)
    {
        Grow(ref _passes, _passCount);
        _passes[_passCount] = new Pass(_passes[_passCount - 1].Target, null, _drawCount, copy);
        _passCount++;
        _open = false;
    }

    // The surface pixels per unit the offsets of the reads drawn next are in.
    internal void SetReadScale(float scale)
    {
        _open &= scale == _state.ReadScale;
        _state.ReadScale = scale;
    }

    // Grows the current read's copy over a world rect, moved by shift onto the frame and placed through the
    // transform, plus the reach of a read past its sprite. Only the pixels a read can reach are copied.
    internal void Widen(in Rect rect, Vector2 shift)
    {
        ref Area copied = ref _passes[_passCount - 1].Copied;
        float reach = ReadReach * _state.ReadScale;
        Vector2 low = Vector2.Transform(new Vector2(rect.Left, rect.Top) + shift, _state.Transform);
        Vector2 high = Vector2.Transform(new Vector2(rect.Right, rect.Bottom) + shift, _state.Transform);
        Area viewport = _state.Viewport;
        int left = Math.Max(viewport.X, (int)MathF.Floor(viewport.X + low.X - reach));
        int top = Math.Max(viewport.Y, (int)MathF.Floor(viewport.Y + low.Y - reach));
        int right = Math.Min(viewport.X + viewport.Width, (int)MathF.Ceiling(viewport.X + high.X + reach));
        int bottom = Math.Min(viewport.Y + viewport.Height, (int)MathF.Ceiling(viewport.Y + high.Y + reach));
        if (right <= left || bottom <= top)
        {
            return;
        }

        if (copied.Width > 0)
        {
            left = Math.Min(left, copied.X);
            top = Math.Min(top, copied.Y);
            right = Math.Max(right, copied.X + copied.Width);
            bottom = Math.Max(bottom, copied.Y + copied.Height);
        }

        copied = new Area(left, top, right - left, bottom - top);
    }

    // The rect of the target the quads drawn next land in. The transform maps into it.
    internal void SetViewport(int x, int y, int width, int height)
    {
        Area viewport = new(x, y, width, height);
        _open &= viewport == _state.Viewport;
        _state.Viewport = viewport;
    }

    // Maps the quads drawn next to the viewport's pixels.
    internal void SetTransform(in Matrix4x4 transform)
    {
        _open &= transform == _state.Transform;
        _state.Transform = transform;
    }

    internal void SetSampling(TextureSampling sampling)
    {
        GpuSampler sampler = _samplers.Get(sampling);
        _open &= ReferenceEquals(sampler, _state.Sampler);
        _state.Sampler = sampler;
    }

    internal void SetBlend(in GpuBlendState blend)
    {
        _open &= blend == _state.Blend;
        _state.Blend = blend;
    }

    // The material the sprites drawn next use, null for Capsule's own sprite shader. Its textures load
    // now, and their uploads join the frame's.
    internal void SetMaterial(Material? material)
    {
        if (!ReferenceEquals(material, _state.Material) && material is not null)
        {
            _shaders.Prepare(material);
        }

        _open &= ReferenceEquals(material, _state.Material);
        _state.Material = material;
    }

    // Cuts the quads drawn next to a rect of the target, in its pixels.
    internal void SetClip(int x, int y, int width, int height)
    {
        Area clip = new(x, y, width, height);
        _open &= clip == _state.Clip;
        _state.Clip = clip;
    }

    internal void ClearClip()
    {
        _open &= _state.Clip is null;
        _state.Clip = null;
    }

    // Where the sprites drawn next are placed from: their layer's corner, the frame's corner it lands
    // on, and the surface pixels per unit they snap to, zero for none.
    internal void SetPlacement(Vector2 layerCorner, Vector2 frameCorner, float grid)
    {
        _open &= layerCorner == _state.LayerCorner && frameCorner == _state.FrameCorner && grid == _state.Grid;
        _state.LayerCorner = layerCorner;
        _state.FrameCorner = frameCorner;
        _state.Grid = grid;
    }

    // A sprite whose region sits at offsetX, offsetY on texture, the page it is resident on.
    internal void Add(SampledTexture texture, in SpriteIntent sprite, int offsetX, int offsetY)
    {
        TextureRegion region = sprite.Sprite.Region;
        ref SpriteInstance instance = ref Open(texture);
        instance.PreviousPosition = sprite.PreviousPosition;
        instance.Position = sprite.Position;
        instance.PreviousRotation = sprite.PreviousRotation;
        instance.Rotation = sprite.Rotation;
        instance.Origin = sprite.DrawOrigin;
        instance.Size = sprite.Size;
        instance.Region = SpriteInstance.Pack(region.X + offsetX, region.Y + offsetY);
        instance.Extent = SpriteInstance.Pack(region.Width, region.Height);
        instance.Tint = Tint(sprite.Color, sprite.Blend);
        instance.Flash = Flash(sprite.Flash, sprite.Color.A);
        instance.Flags = FlagsOf(in sprite);
    }

    // Consecutive sprites whose regions sit at offsetX, offsetY on texture, as Add records each. Neighbours
    // mostly share a colour, and its packing is worked out where it changes.
    internal void AddRange(SampledTexture texture, ReadOnlySpan<SpriteIntent> sprites, int offsetX, int offsetY)
    {
        Span<SpriteInstance> instances = Open(texture, sprites.Length);
        ColorRgba color = default;
        BlendMode blend = BlendMode.Alpha;
        uint tint = 0;
        for (int index = 0; index < sprites.Length; index++)
        {
            ref readonly SpriteIntent sprite = ref sprites[index];
            if (index == 0 || sprite.Color != color || sprite.Blend != blend)
            {
                color = sprite.Color;
                blend = sprite.Blend;
                tint = Tint(color, blend);
            }

            TextureRegion region = sprite.Sprite.Region;
            ref SpriteInstance instance = ref instances[index];
            instance.PreviousPosition = sprite.PreviousPosition;
            instance.Position = sprite.Position;
            instance.PreviousRotation = sprite.PreviousRotation;
            instance.Rotation = sprite.Rotation;
            instance.Origin = sprite.DrawOrigin;
            instance.Size = sprite.Size;
            instance.Region = SpriteInstance.Pack(region.X + offsetX, region.Y + offsetY);
            instance.Extent = SpriteInstance.Pack(region.Width, region.Height);
            instance.Tint = tint;
            instance.Flash = Flash(sprite.Flash, color.A);
            instance.Flags = FlagsOf(in sprite);
        }
    }

    // The way the texels read: the intent's flips over the frame's own.
    internal static SpriteFlags FlagsOf(in SpriteIntent sprite) =>
        (sprite.MirrorsX ? SpriteFlags.FlipX : SpriteFlags.None) | (sprite.MirrorsY ? SpriteFlags.FlipY : SpriteFlags.None);

    // The entire texture where it stands: position is where origin lands, origin is in texels, scale is
    // drawn units per texel, and rotation is radians clockwise about origin. It blends as alpha.
    internal void AddWhole(SampledTexture texture, Vector2 position, Vector2 origin, Vector2 scale, float rotation, ColorRgba color)
    {
        ref SpriteInstance instance = ref Open(texture);
        instance.PreviousPosition = position;
        instance.Position = position;
        instance.PreviousRotation = rotation;
        instance.Rotation = rotation;
        instance.Origin = origin;
        instance.Size = scale;
        instance.Region = 0;
        instance.Extent = SpriteInstance.Pack(texture.Texture.Width, texture.Texture.Height);
        instance.Tint = Tint(color, BlendMode.Alpha);
        instance.Flash = 0;
        instance.Flags = SpriteFlags.Whole;
    }

    // One triangle of a polygon on texture, the white texel's page, each corner given at both ends of the step.
    // Every triangle of one polygon starts at its first point, which the vertex stage snaps for them all.
    internal void AddTriangle(SampledTexture texture, Vector2 previousA, Vector2 a, Vector2 previousB, Vector2 b, Vector2 previousC, Vector2 c, uint tint, uint flash)
    {
        ref SpriteInstance instance = ref Open(texture);
        instance.PreviousPosition = previousA;
        instance.Position = a;
        instance.PreviousRotation = previousB.X;
        instance.Rotation = previousB.Y;
        instance.Origin = b;
        instance.Size = previousC;
        instance.Region = BitConverter.SingleToUInt32Bits(c.X);
        instance.Extent = BitConverter.SingleToUInt32Bits(c.Y);
        instance.Tint = tint;
        instance.Flash = flash;
        instance.Flags = SpriteFlags.Triangle;
    }

    // Uploads every recorded instance in the frame's copy pass. The upload cycles the buffer, and draws
    // recorded before it keep reading the instances they were recorded with.
    internal void Upload(GpuCopyPass copy)
    {
        int bytes = _count * Unsafe.SizeOf<SpriteInstance>();
        if (_buffer is null || _buffer.Size < bytes)
        {
            _buffer?.Dispose();
            _buffer = _device.CreateBuffer(GpuBufferUsage.GraphicsStorageRead, _instances.Length * Unsafe.SizeOf<SpriteInstance>());
        }

        copy.UploadToBuffer(_buffer, 0, MemoryMarshal.AsBytes(_instances.AsSpan(0, _count)), cycle: true);
    }

    // Records each pass into commands.
    internal void Execute(GpuCommandBuffer commands)
    {
        for (int p = 0; p < _passCount; p++)
        {
            Pass recorded = _passes[p];
            GpuTexture target = recorded.Target;
            if (recorded.Screen is { } screen && recorded.Copied.Width > 0)
            {
                Area area = recorded.Copied;
                GpuCopyPass copying = commands.BeginCopyPass();
                copying.CopyTexture(target, screen.Texture, area.X, area.Y, area.Width, area.Height);
                copying.End();
            }

            GpuRenderPass pass = commands.BeginRenderPass(recorded.Clear is { } clear
                ? new GpuColorTarget(target, GpuLoadOp.Clear, Premultiplied(clear))
                : new GpuColorTarget(target, GpuLoadOp.Load));
            pass.BindVertexStorageBuffer(0, _buffer!);
            int last = p + 1 < _passCount ? _passes[p + 1].FirstDraw : _drawCount;
            Execute(commands, pass, in recorded, _draws.AsSpan(recorded.FirstDraw, last - recorded.FirstDraw));
            pass.End();
        }
    }

    // A tint as the premultiplied blend draws it. An alpha intent is premultiplied. An additive intent
    // is scaled by its alpha and packs alpha zero, which adds src.rgb + dst.rgb x (1 - 0) through the
    // same pipeline. Bytes R, G, B, A in order.
    internal static uint Tint(ColorRgba color, BlendMode blend) => blend == BlendMode.Additive
        ? Bytes(ColorRgba.Multiply(color.R, color.A), ColorRgba.Multiply(color.G, color.A), ColorRgba.Multiply(color.B, color.A), 0)
        : Bytes(color.R * color.A / 255, color.G * color.A / 255, color.B * color.A / 255, color.A);

    // A flash colour scaled by the sprite's straight tint alpha, with the amount in alpha. A flash fades
    // as the sprite does under either blend. A zero amount packs zero, which leaves the shader's colour
    // exactly as it was.
    internal static uint Flash(ColorRgba flash, byte tintAlpha) => flash.A == 0
        ? 0u
        : Bytes(ColorRgba.Multiply(flash.R, tintAlpha), ColorRgba.Multiply(flash.G, tintAlpha), ColorRgba.Multiply(flash.B, tintAlpha), flash.A);

    public void Dispose() => _buffer?.Dispose();

    // The product a then b, summed in a fixed order, which keeps a transform the same to the bit
    // wherever it is computed.
    internal static Matrix4x4 Multiply(in Matrix4x4 a, in Matrix4x4 b) => new(
        (((a.M11 * b.M11) + (a.M12 * b.M21)) + (a.M13 * b.M31)) + (a.M14 * b.M41),
        (((a.M11 * b.M12) + (a.M12 * b.M22)) + (a.M13 * b.M32)) + (a.M14 * b.M42),
        (((a.M11 * b.M13) + (a.M12 * b.M23)) + (a.M13 * b.M33)) + (a.M14 * b.M43),
        (((a.M11 * b.M14) + (a.M12 * b.M24)) + (a.M13 * b.M34)) + (a.M14 * b.M44),
        (((a.M21 * b.M11) + (a.M22 * b.M21)) + (a.M23 * b.M31)) + (a.M24 * b.M41),
        (((a.M21 * b.M12) + (a.M22 * b.M22)) + (a.M23 * b.M32)) + (a.M24 * b.M42),
        (((a.M21 * b.M13) + (a.M22 * b.M23)) + (a.M23 * b.M33)) + (a.M24 * b.M43),
        (((a.M21 * b.M14) + (a.M22 * b.M24)) + (a.M23 * b.M34)) + (a.M24 * b.M44),
        (((a.M31 * b.M11) + (a.M32 * b.M21)) + (a.M33 * b.M31)) + (a.M34 * b.M41),
        (((a.M31 * b.M12) + (a.M32 * b.M22)) + (a.M33 * b.M32)) + (a.M34 * b.M42),
        (((a.M31 * b.M13) + (a.M32 * b.M23)) + (a.M33 * b.M33)) + (a.M34 * b.M43),
        (((a.M31 * b.M14) + (a.M32 * b.M24)) + (a.M33 * b.M34)) + (a.M34 * b.M44),
        (((a.M41 * b.M11) + (a.M42 * b.M21)) + (a.M43 * b.M31)) + (a.M44 * b.M41),
        (((a.M41 * b.M12) + (a.M42 * b.M22)) + (a.M43 * b.M32)) + (a.M44 * b.M42),
        (((a.M41 * b.M13) + (a.M42 * b.M23)) + (a.M43 * b.M33)) + (a.M44 * b.M43),
        (((a.M41 * b.M14) + (a.M42 * b.M24)) + (a.M43 * b.M34)) + (a.M44 * b.M44));

    // One pass's draws, binding only the state each changes from the one before.
    private void Execute(GpuCommandBuffer commands, GpuRenderPass pass, in Pass recorded, ReadOnlySpan<Draw> draws)
    {
        SampledTexture? screen = recorded.Screen;
        float readScale = 0f;
        Area full = new(0, 0, recorded.Target.Width, recorded.Target.Height);
        Area viewport = full;
        Area scissor = full;
        Matrix4x4 transform = default;
        Area? projectedFor = null;
        Matrix4x4 projected = default;
        GpuPipeline? pipeline = null;
        Material? material = null;
        GpuSampler? materialSampler = null;
        GpuTextureSamplerBinding sprite = default;
        foreach (ref readonly Draw draw in draws)
        {
            ref readonly State state = ref draw.State;
            if (state.Viewport != viewport)
            {
                viewport = state.Viewport;
                pass.SetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
            }

            Area clip = state.Clip ?? full;
            if (clip != scissor)
            {
                scissor = clip;
                pass.SetScissor(clip.X, clip.Y, clip.Width, clip.Height);
            }

            // Each binds only when it changes. A material's samplers and uniforms outlive a pipeline bind, and
            // a pipeline bind makes the GPU rebind every table, which a material change alone does not.
            GpuPipeline next = _shaders.Pipeline(state.Material, state.Blend);
            if (!ReferenceEquals(next, pipeline))
            {
                pass.BindGraphicsPipeline(next);
                pipeline = next;
            }

            // A material texture with no sampling of its own reads through the draw's sampler.
            if (!ReferenceEquals(state.Material, material) || !ReferenceEquals(state.Sampler, materialSampler))
            {
                _shaders.BindMaterial(commands, pass, state.Material, state.Sampler, screen);
                material = state.Material;
                materialSampler = state.Sampler;
            }

            if (screen is not null && state.ReadScale != readScale)
            {
                readScale = state.ReadScale;
                PushRead(commands, recorded.Copied, readScale);
            }

            GpuTexture texture = draw.Texture.Texture;
            GpuTextureSamplerBinding binding = new(texture, draw.Texture.Sampler ?? state.Sampler);
            if (binding != sprite)
            {
                sprite = binding;
                pass.BindFragmentSamplers(0, new ReadOnlySpan<GpuTextureSamplerBinding>(in sprite));
            }

            if (state.Transform != transform || state.Viewport != projectedFor)
            {
                transform = state.Transform;
                projectedFor = state.Viewport;
                projected = Multiply(transform, Projection(state.Viewport));
            }

            SpriteUniforms uniforms = new()
            {
                Transform = projected,
                TexelSize = new Vector2(1f / texture.Width, 1f / texture.Height),
                LayerCorner = state.LayerCorner,
                FrameCorner = state.FrameCorner,
                Alpha = _alpha,
                Grid = state.Grid,
                Coverage = texture.Format == GpuTextureFormat.R8Unorm ? 1f : 0f,
                FirstSprite = (uint)draw.First,
            };
            commands.PushVertexUniformData(0, in uniforms);
            pass.DrawPrimitives(draw.Count * 6, 1, 0, 0);
        }
    }

    // Clip space of the viewport: an orthographic projection with Y down and the origin at the
    // viewport's top-left corner.
    private static Matrix4x4 Projection(in Area viewport) => new(
        2f / viewport.Width, 0f, 0f, 0f,
        0f, 2f / (0f - viewport.Height), 0f, 0f,
        0f, 0f, 1f, 0f,
        -1f, 1f, 0f, 1f);

    // A read's uniforms, in the buffer after the material's and the scene globals': the copy's pixel centres it stays
    // inside and its scale, as the pixel stage lays them out.
    private static void PushRead(GpuCommandBuffer commands, in Area area, float scale)
    {
        Span<float> values = stackalloc float[8];
        values[0] = area.X + 0.5f;
        values[1] = area.Y + 0.5f;
        values[2] = area.X + area.Width - 0.5f;
        values[3] = area.Y + area.Height - 0.5f;
        values[4] = scale;
        commands.PushFragmentUniformData(2, MemoryMarshal.AsBytes(values));
    }

    private static uint Bytes(int r, int g, int b, int a) => (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    private static Vector4 Premultiplied(ColorRgba color) => new(
        (color.R * color.A / 255) / 255f,
        (color.G * color.A / 255) / 255f,
        (color.B * color.A / 255) / 255f,
        color.A / 255f);

    private static void Grow<T>(ref T[] array, int count)
    {
        if (count == array.Length)
        {
            Array.Resize(ref array, array.Length * 2);
        }
    }

    // The instance the next quad writes, in the open draw if it shares texture, or in a new one.
    private ref SpriteInstance Open(SampledTexture texture)
    {
        if (!_open || !ReferenceEquals(_draws[_drawCount - 1].Texture, texture))
        {
            Grow(ref _draws, _drawCount);
            _draws[_drawCount++] = new Draw(_state, texture, _count);
            _open = true;
        }

        Grow(ref _instances, _count);
        _draws[_drawCount - 1].Count++;

        return ref _instances[_count++];
    }

    // The instances the next count quads write, in the open draw if it shares texture, or in a new one.
    private Span<SpriteInstance> Open(SampledTexture texture, int count)
    {
        if (!_open || !ReferenceEquals(_draws[_drawCount - 1].Texture, texture))
        {
            Grow(ref _draws, _drawCount);
            _draws[_drawCount++] = new Draw(_state, texture, _count);
            _open = true;
        }

        if (_count + count > _instances.Length)
        {
            Array.Resize(ref _instances, Math.Max(_instances.Length * 2, _count + count));
        }

        _draws[_drawCount - 1].Count += count;
        Span<SpriteInstance> instances = _instances.AsSpan(_count, count);
        _count += count;

        return instances;
    }

    private readonly record struct Area(int X, int Y, int Width, int Height);

    private struct State
    {
        internal Area Viewport;
        internal Area? Clip;
        internal Matrix4x4 Transform;
        internal GpuSampler Sampler;
        internal GpuBlendState Blend;
        internal Material? Material;
        internal Vector2 LayerCorner;
        internal Vector2 FrameCorner;
        internal float Grid;
        internal float ReadScale;
    }

    // Count instances from First, drawn with Texture under State.
    private struct Draw(State state, SampledTexture texture, int first)
    {
        internal readonly State State = state;
        internal readonly SampledTexture Texture = texture;
        internal readonly int First = first;
        internal int Count;
    }

    // The pass runs from FirstDraw to the next pass's. One that opens on a read of the picture copies Copied into Screen.
    private record struct Pass(GpuTexture Target, ColorRgba? Clear, int FirstDraw, SampledTexture? Screen = null)
    {
        internal Area Copied;
    }
}
