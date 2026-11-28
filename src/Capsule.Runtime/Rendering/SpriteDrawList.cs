using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// A frame's quads, recorded as one sprite instance each in submission order, and the draws that cover
// them. Consecutive quads on one texture under the same state are one draw. Once recorded, the frame
// reaches the device as its uploads and then its passes, each draw binding only the state that changed.
// The sprite vertex stage interpolates, snaps and places each instance. Every array grows to the
// largest frame and is kept, and a frame at steady state allocates nothing.
//
// An instance stored from render intent depends on that intent and the page its texture is on, and never
// on the frame's fraction of a step. Those instances are kept, and a frame that draws the same intent again
// reuses them. A reuse walks the frame as a recording does and sets every draw's state anew, but writes and
// uploads none of the kept instances. Each run of them it adds must match the run the recording logged in
// the same place, or the reuse has diverged and the frame records again. The quads placed from the frame's
// own corners, such as the world's clear and every line, are written every frame into a buffer of their own.
// A retained buffer, such as a chunk of tiles, is drawn from its owner's buffer and written by its owner.
internal sealed class SpriteDrawList : IDisposable
{
    // Whether each reuse that matched its recording records the frame too and throws where any kept instance
    // differs. A windowed run with it on checks every frame it reuses.
    private static readonly bool ChecksReuse = AppContext.TryGetSwitch("Capsule.CheckReuse", out bool on) && on;

    private readonly GpuDevice _device;
    private readonly ShaderStore _shaders;
    private readonly TextureSamplers _samplers;

    // The light every draw that sets none is multiplied by, the white texel.
    private readonly SampledTexture _unlit;

    // How far past its sprite a read of the picture reaches before it repeats the copy's edge, in the sprite's
    // units. The copy stops there, and a small strip does not pay for the whole surface.
    private const float ReadReach = 64f;

    private SpriteInstance[] _instances = new SpriteInstance[1024];
    private int _count;
    private Draw[] _draws = new Draw[64];
    private int _drawCount;
    private Pass[] _passes = new Pass[8];
    private int _passCount;
    private readonly InstanceBuffer _buffer = new();

    // The quads the frame writes every time, which draws read from their own buffer.
    private SpriteInstance[] _frameInstances = new SpriteInstance[64];
    private int _frameCount;
    private readonly InstanceBuffer _frameBuffer = new();

    // The runs of kept instances the last recording added, one per texture and page offset, and whether
    // there is a recording to reuse.
    private Segment[] _segments = new Segment[64];
    private int _segmentCount;
    private bool _recorded;

    // While a reuse walks: whether it has left its log, the logged run it is inside and how much of it the
    // walk has added so far. A reuse that diverged writes nothing for the rest of its walk.
    private bool _reusing;
    private bool _diverged;
    private int _segment;
    private int _consumed;

    // Under the check switch, the kept instances of a matched reuse and how many there are, which the recording
    // made after it is compared with. Below zero, no check is pending.
    private SpriteInstance[] _checked = [];
    private int _checkedCount = -1;

    // What the next quad draws under. A change closes the open draw.
    private State _state;
    private bool _open;

    internal SpriteDrawList(GpuDevice device, ShaderStore shaders, TextureSamplers samplers, SampledTexture unlit)
    {
        _device = device;
        _shaders = shaders;
        _samplers = samplers;
        _unlit = unlit;
    }

    // Begins a walk. A recording forgets the last one. A reuse, where there is a recording to reuse, keeps its
    // kept instances and checks the walk against its log.
    internal void Reset(bool reuse)
    {
        _count = 0;
        _frameCount = 0;
        _drawCount = 0;
        _passCount = 0;
        _open = false;
        _reusing = reuse && _recorded;
        _diverged = false;
        _segment = 0;
        _consumed = 0;
        if (!_reusing)
        {
            _segmentCount = 0;
            _recorded = false;
        }
    }

    // Ends the walk, and returns whether it holds. A recording always does, and can be reused from here. A
    // reuse holds where it added every logged run and no more. One that does not leaves the frame to record
    // again. So does one the check switch compares with a recording, which CompareCheck then does.
    internal bool Finish()
    {
        if (!_reusing)
        {
            _recorded = true;
            return true;
        }

        _diverged |= _segment != _segmentCount || _consumed != 0;
        if (_diverged || !Development.IsSupported || !ChecksReuse)
        {
            return !_diverged;
        }

        if (_checked.Length < _count)
        {
            _checked = new SpriteInstance[_instances.Length];
        }

        _instances.AsSpan(0, _count).CopyTo(_checked);
        _checkedCount = _count;

        return false;
    }

    // Throws where the recording just made differs from the reuse Finish copied aside, if it copied one.
    internal void CompareCheck()
    {
        if (_checkedCount < 0)
        {
            return;
        }

        int reused = _checkedCount;
        _checkedCount = -1;
        ReadOnlySpan<SpriteInstance> before = _checked.AsSpan(0, reused);
        ReadOnlySpan<SpriteInstance> after = _instances.AsSpan(0, _count);
        if (!MemoryMarshal.AsBytes(before).SequenceEqual(MemoryMarshal.AsBytes(after)))
        {
            throw new InvalidOperationException(
                $"A reused frame kept {reused} instances that a recording of the same intent writes differently, as {_count}. An instance stored from intent read something that changes between frames.");
        }
    }

    // Draws what follows into target after clearing it to clear, a straight-alpha colour, unless that is
    // null. The pass starts over the whole target with no transform, point sampling, premultiplied
    // blending, Capsule's own shader, no light, and no clip or placement.
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

    // The light the sprites drawn next see at their pixel, at the target's extent, or null for none, which binds
    // the white texel. A lit sprite is multiplied by it. An unlit one takes its shader's unlit form, which reads the
    // light only where its fragment calls SampleLight.
    internal void SetLight(SampledTexture? light, bool lit)
    {
        lit &= light is not null;
        _open &= ReferenceEquals(light, _state.Light) && lit == _state.Lit;
        _state.Light = light;
        _state.Lit = lit;
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

    // Cuts the quads drawn next to a rect of the target, in its pixels. An empty rect draws none of them.
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

    // Where the sprites drawn next are placed.
    internal void SetPlacement(in GridPlacement placement)
    {
        _open &= placement == _state.Placement;
        _state.Placement = placement;
    }

    // A sprite whose region sits at offsetX, offsetY on texture, the page it is resident on, with its tint's colour
    // drawn intensity times.
    internal void Add(SampledTexture texture, in SpriteIntent sprite, int offsetX, int offsetY, float intensity = 1f)
    {
        if (!Reserve(texture, offsetX, offsetY, 1))
        {
            return;
        }

        Fill(ref _instances[_count - 1], in sprite, offsetX, offsetY, Tint(sprite.Color, sprite.Blend), intensity);
    }

    // Consecutive sprites whose regions sit at offsetX, offsetY on texture, as Add records each. Neighbours
    // mostly share a colour, and its packing is worked out where it changes.
    internal void AddRange(SampledTexture texture, ReadOnlySpan<SpriteIntent> sprites, int offsetX, int offsetY)
    {
        if (!Reserve(texture, offsetX, offsetY, sprites.Length))
        {
            return;
        }

        Write(sprites, _instances.AsSpan(_count - sprites.Length, sprites.Length), offsetX, offsetY);
    }

    // Writes an instance for each sprite, as AddRange records them.
    internal static void Write(ReadOnlySpan<SpriteIntent> sprites, Span<SpriteInstance> instances, int offsetX, int offsetY)
    {
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

            Fill(ref instances[index], in sprite, offsetX, offsetY, tint, 1f);
        }
    }

    // Sprites whose regions sit at offsetX, offsetY on texture, written this frame among the frame's own quads.
    internal void AddFrameRange(SampledTexture texture, ReadOnlySpan<SpriteIntent> sprites, int offsetX, int offsetY)
    {
        if (sprites.IsEmpty)
        {
            return;
        }

        Join(texture, _frameBuffer, _frameCount).Count += sprites.Length;
        if (_frameCount + sprites.Length > _frameInstances.Length)
        {
            Array.Resize(ref _frameInstances, Math.Max(_frameInstances.Length * 2, _frameCount + sprites.Length));
        }

        Write(sprites, _frameInstances.AsSpan(_frameCount, sprites.Length), offsetX, offsetY);
        _frameCount += sprites.Length;
    }

    // Count instances from first in buffer, whose regions sit on texture. The draw is its own, and joins no other.
    internal void AddRetained(SampledTexture texture, InstanceBuffer buffer, int first, int count)
    {
        Grow(ref _draws, _drawCount);
        _draws[_drawCount++] = new Draw(_state, texture, first, buffer) { Count = count };
        _open = false;
    }

    // The way the texels read: the intent's flips over the frame's own.
    internal static SpriteFlags FlagsOf(in SpriteIntent sprite) =>
        (sprite.MirrorsX ? SpriteFlags.FlipX : SpriteFlags.None) | (sprite.MirrorsY ? SpriteFlags.FlipY : SpriteFlags.None);

    // The entire texture where it stands: position is where origin lands, origin is in texels, scale is
    // drawn units per texel, and rotation is radians clockwise about origin. It blends as alpha.
    internal void AddWhole(SampledTexture texture, Vector2 position, Vector2 origin, Vector2 scale, float rotation, ColorRgba color)
    {
        ref SpriteInstance instance = ref OpenFrame(texture);
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
        instance.Intensity = 1f;
    }

    // One triangle of a polygon on texture, the white texel's page, each corner given at both ends of the step.
    // Every triangle of one polygon starts at its first point, which the vertex stage snaps for them all. Its
    // tint's colour is drawn intensity times.
    internal void AddTriangle(SampledTexture texture, Vector2 previousA, Vector2 a, Vector2 previousB, Vector2 b, Vector2 previousC, Vector2 c, uint tint, uint flash, float intensity = 1f)
    {
        if (!Reserve(texture, 0, 0, 1))
        {
            return;
        }

        ref SpriteInstance instance = ref _instances[_count - 1];
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
        instance.Intensity = intensity;
    }

    // Uploads the frame's own quads in the frame's copy pass, and the kept instances unless the frame reuses
    // them, and returns the bytes it uploaded. Each upload cycles its buffer, and draws recorded before it keep
    // reading the instances they were recorded with.
    internal int Upload(GpuCopyPass copy)
    {
        int bytes = 0;
        if (!_reusing && _count > 0)
        {
            bytes += _buffer.Upload(_device, copy, _instances.AsSpan(0, _count), _instances.Length);
        }

        if (_frameCount > 0)
        {
            bytes += _frameBuffer.Upload(_device, copy, _frameInstances.AsSpan(0, _frameCount), _frameInstances.Length);
        }

        return bytes;
    }

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

    public void Dispose()
    {
        _buffer.Dispose();
        _frameBuffer.Dispose();
    }

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
        SampledTexture? boundLight = null;
        GpuBuffer? bound = null;
        foreach (ref readonly Draw draw in draws)
        {
            if (draw.State.Clip is { } cut && (cut.Width <= 0 || cut.Height <= 0))
            {
                continue;
            }

            GpuBuffer instances = draw.Buffer.Gpu!;
            if (!ReferenceEquals(instances, bound))
            {
                pass.BindVertexStorageBuffer(0, instances);
                bound = instances;
            }

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
            GpuPipeline next = _shaders.Pipeline(state.Material, state.Blend, recorded.Target.Format, state.Lit);
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

            // Point sampling reads the light's texel at the pixel's own place, unmixed with its neighbours.
            SampledTexture light = state.Light ?? _unlit;
            if (!ReferenceEquals(light, boundLight))
            {
                boundLight = light;
                GpuTextureSamplerBinding lightBinding = new(light.Texture, _samplers.Get(TextureSampling.Point));
                pass.BindFragmentSamplers(1, new ReadOnlySpan<GpuTextureSamplerBinding>(in lightBinding));
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
                PreviousLayerCorner = state.Placement.PreviousLayerCorner,
                LayerCorner = state.Placement.LayerCorner,
                FrameCorner = state.Placement.FrameCorner,
                Shift = state.Placement.Shift,
                Grids = state.Placement.Grids,
                Alpha = state.Placement.Alpha,
                Grid = state.Placement.Grid,
                Coverage = texture.Format == GpuTextureFormat.R8Unorm ? 1f : 0f,
                FirstSprite = (uint)draw.First,
                LightPlace = LightPlace(state.Viewport, recorded.Target),
            };
            commands.PushVertexUniformData(0, in uniforms);
            pass.DrawPrimitives(draw.Count * 6, 1, 0, 0);
        }
    }

    // The scale and offset from the viewport's clip space to texture coordinates on a light the size of target,
    // which is drawn through the same viewport.
    private static Vector4 LightPlace(in Area viewport, GpuTexture target) => new(
        viewport.Width / (2f * target.Width),
        viewport.Height / (-2f * target.Height),
        (viewport.X + (viewport.Width / 2f)) / target.Width,
        (viewport.Y + (viewport.Height / 2f)) / target.Height);

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

    // The instance of a sprite whose region sits at offsetX, offsetY, with its packed tint drawn intensity times.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Fill(ref SpriteInstance instance, in SpriteIntent sprite, int offsetX, int offsetY, uint tint, float intensity)
    {
        TextureRegion region = sprite.Sprite.Region;
        instance.PreviousPosition = sprite.PreviousPosition;
        instance.Position = sprite.Position;
        instance.PreviousRotation = sprite.PreviousRotation;
        instance.Rotation = sprite.Rotation;
        instance.Origin = sprite.DrawOrigin;
        instance.Size = sprite.Size;
        instance.Region = SpriteInstance.Pack(region.X + offsetX, region.Y + offsetY);
        instance.Extent = SpriteInstance.Pack(region.Width, region.Height);
        instance.Tint = tint;
        instance.Flash = Flash(sprite.Flash, sprite.Color.A);
        instance.Flags = FlagsOf(in sprite);
        instance.Intensity = intensity;
    }

    private static void Grow<T>(ref T[] array, int count)
    {
        if (count == array.Length)
        {
            Array.Resize(ref array, array.Length * 2);
        }
    }

    // Adds count kept instances on texture, whose regions sit at offsetX, offsetY on it, to the open draw if it
    // shares texture and buffer, or to a new one, and returns whether the caller writes them as the last count.
    // A recording logs the run and grows the instances. A reuse writes nothing, and checks the run against the log.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool Reserve(SampledTexture texture, int offsetX, int offsetY, int count)
    {
        Join(texture, _buffer, _count).Count += count;
        _count += count;
        if (_reusing)
        {
            if (!_diverged)
            {
                Match(texture, offsetX, offsetY, count);
            }

            return false;
        }

        if (_count > _instances.Length)
        {
            Array.Resize(ref _instances, Math.Max(_instances.Length * 2, _count));
        }

        if (_segmentCount > 0 && _segments[_segmentCount - 1].Continues(texture, offsetX, offsetY))
        {
            _segments[_segmentCount - 1].Count += count;
        }
        else
        {
            Grow(ref _segments, _segmentCount);
            _segments[_segmentCount++] = new Segment(texture, offsetX, offsetY, count);
        }

        return true;
    }

    // Checks a reuse's run against the log. Runs are logged back to back. A walk whose runs match every
    // texture and offset in order has also landed on the same instances.
    private void Match(SampledTexture texture, int offsetX, int offsetY, int count)
    {
        if (_segment == _segmentCount)
        {
            _diverged = true;
            return;
        }

        ref readonly Segment logged = ref _segments[_segment];
        if (!logged.Continues(texture, offsetX, offsetY) || _consumed + count > logged.Count)
        {
            _diverged = true;
            return;
        }

        _consumed += count;
        if (_consumed == logged.Count)
        {
            _segment++;
            _consumed = 0;
        }
    }

    // The instance the next frame quad writes, in the open draw if it shares texture and buffer, or in a new one.
    private ref SpriteInstance OpenFrame(SampledTexture texture)
    {
        Join(texture, _frameBuffer, _frameCount).Count++;
        Grow(ref _frameInstances, _frameCount);

        return ref _frameInstances[_frameCount++];
    }

    // The open draw where it shares texture and buffer, or a new one whose instances start at first.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Draw Join(SampledTexture texture, InstanceBuffer buffer, int first)
    {
        if (!_open || !ReferenceEquals(_draws[_drawCount - 1].Texture, texture) || !ReferenceEquals(_draws[_drawCount - 1].Buffer, buffer))
        {
            Grow(ref _draws, _drawCount);
            _draws[_drawCount++] = new Draw(_state, texture, first, buffer);
            _open = true;
        }

        return ref _draws[_drawCount - 1];
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
        internal GridPlacement Placement;
        internal float ReadScale;
        internal SampledTexture? Light;
        internal bool Lit;
    }

    // Count instances from First in Buffer, drawn with Texture under State.
    private struct Draw(State state, SampledTexture texture, int first, InstanceBuffer buffer)
    {
        internal readonly State State = state;
        internal readonly SampledTexture Texture = texture;
        internal readonly int First = first;
        internal readonly InstanceBuffer Buffer = buffer;
        internal int Count;
    }

    // Count kept instances added back to back on Texture, their regions at OffsetX, OffsetY on it.
    private record struct Segment(SampledTexture Texture, int OffsetX, int OffsetY, int Count)
    {
        internal readonly bool Continues(SampledTexture texture, int offsetX, int offsetY) =>
            ReferenceEquals(Texture, texture) && OffsetX == offsetX && OffsetY == offsetY;
    }

    // The pass runs from FirstDraw to the next pass's. One that opens on a read of the picture copies Copied into Screen.
    private record struct Pass(GpuTexture Target, ColorRgba? Clear, int FirstDraw, SampledTexture? Screen = null)
    {
        internal Area Copied;
    }
}

// A device buffer of sprite instances, created at the first upload and grown to hold the largest. A draw names the
// buffer and reads whatever the upload before it wrote.
internal sealed class InstanceBuffer : IDisposable
{
    internal GpuBuffer? Gpu { get; private set; }

    // Writes instances from the buffer's front, growing it to capacity instances where they do not fit, and returns
    // the bytes it uploaded. The upload cycles the buffer, and draws recorded before it keep reading the instances
    // they were recorded with.
    internal int Upload(GpuDevice device, GpuCopyPass copy, ReadOnlySpan<SpriteInstance> instances, int capacity)
    {
        int bytes = instances.Length * Unsafe.SizeOf<SpriteInstance>();
        if (Gpu is null || Gpu.Size < bytes)
        {
            Gpu?.Dispose();
            Gpu = device.CreateBuffer(GpuBufferUsage.GraphicsStorageRead, Math.Max(capacity, instances.Length) * Unsafe.SizeOf<SpriteInstance>());
        }

        copy.UploadToBuffer(Gpu, 0, MemoryMarshal.AsBytes(instances), cycle: true);

        return bytes;
    }

    // Writes instances in place from first, in a buffer of capacity instances created at the first write, and
    // returns the bytes it uploaded. The rest of the buffer keeps what it held, and the device orders the write after
    // the frames drawn before it.
    internal int Write(GpuDevice device, GpuCopyPass copy, int first, ReadOnlySpan<SpriteInstance> instances, int capacity)
    {
        Gpu ??= device.CreateBuffer(GpuBufferUsage.GraphicsStorageRead, capacity * Unsafe.SizeOf<SpriteInstance>());
        copy.UploadToBuffer(Gpu, first * Unsafe.SizeOf<SpriteInstance>(), MemoryMarshal.AsBytes(instances), cycle: false);

        return instances.Length * Unsafe.SizeOf<SpriteInstance>();
    }

    public void Dispose()
    {
        Gpu?.Dispose();
        Gpu = null;
    }
}
