using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;

namespace Capsule.Runtime.Gpu;

// SDL_GPUDevice: creates the GPU resources and the command buffers that use them. Its bodies drive
// MonoGame's OpenGL device until the host draws through SDL's GPU API. A command takes effect when it
// is recorded. OpenGL reads no storage buffer, and a draw runs the vertex stage on the CPU through
// CpuVertexStage into staged vertices, with a copy of the effect state it draws under. The staged
// draws reach the device as one upload to a ring of vertices when the target, viewport or scissor
// changes or the stage fills, then a draw each, applying the effect where its state differs from the
// draw before. A frame at steady state allocates nothing.
internal sealed class GpuDevice : IDisposable
{
    // One upload's worth of vertices, a whole number of triangles.
    private const int ChunkVertices = 8192 * 6;

    // The vertex buffer is a ring of chunks appended with NoOverwrite, and an upload never writes over
    // vertices an unfinished draw may still be reading. At the end of the ring the cursor wraps and
    // the write is Discard, which orphans the store instead of stalling behind those draws.
    private const int RingChunks = 4;

    private const int UniformBytes = 256;

    private static readonly RasterizerState Unscissored = RasterizerState.CullCounterClockwise;

    private static readonly RasterizerState Scissored = new()
    {
        CullMode = CullMode.CullCounterClockwiseFace,
        ScissorTestEnable = true,
    };

    private readonly GraphicsDevice _graphics;
    private readonly GpuTexture _backBuffer;

    private readonly byte[] _vertexUniforms = new byte[UniformBytes];
    private readonly byte[][] _fragmentUniforms = [new byte[UniformBytes], new byte[UniformBytes]];
    private readonly int[] _fragmentLengths = new int[2];
    private readonly GpuTextureSamplerBinding[] _samplers = new GpuTextureSamplerBinding[16];

    private readonly Run[] _runs = new Run[ChunkVertices / 3];
    private int _runCount;

    // The effect states the staged runs draw under, and the bytes and bindings they copied, each grown to
    // the largest upload and kept.
    private State[] _states = new State[64];
    private int _stateCount;
    private byte[] _stateBytes = new byte[64 * UniformBytes];
    private int _stateBytesUsed;
    private GpuTextureSamplerBinding[] _stateSamplers = new GpuTextureSamplerBinding[64];
    private int _stateSamplersUsed;

    // Whether the next staged run copies the effect state afresh, which every change to it calls for.
    private bool _stateChanged = true;

    // The render target OpenGL has bound, null for the back buffer.
    private RenderTarget2D? _bound;

    private GpuTexture? _target;
    private Rectangle _viewport;
    private Rectangle? _scissor;
    private GpuPipeline? _pipeline;
    private GpuBuffer? _storage;

    private DynamicVertexBuffer? _vertices;

    // Three coincident vertices, which link an effect's program at pipeline creation.
    private VertexBuffer? _degenerate;
    private byte[] _staging = [];
    private int _stride;

    // Vertices staged and not yet uploaded, and vertices of the ring already handed to draws since the
    // last wrap.
    private int _staged;
    private int _cursor;

    // The shader format whose files this device creates shaders from.
    internal string ShaderFormat => "glsl";

    internal GpuTextureFormat SwapchainTextureFormat => GpuTextureFormat.R8G8B8A8Unorm;

    // The window's drawable extent in pixels.
    internal (int Width, int Height) SwapchainSize =>
        (_graphics.PresentationParameters.BackBufferWidth, _graphics.PresentationParameters.BackBufferHeight);

    // Runs the vertex stage for the OpenGL effects. The renderer that owns the vertex stage sets it.
    internal ICpuVertexStage? CpuVertexStage { get; set; }

    internal GpuDevice(GraphicsDevice graphics)
    {
        _graphics = graphics;
        _backBuffer = new GpuTexture(null, GpuTextureFormat.R8G8B8A8Unorm, 0, 0);
    }

    internal GpuCommandBuffer AcquireCommandBuffer() => new(this);

    internal GpuTexture CreateTexture(in GpuTextureInfo info)
    {
        Texture2D body = (info.Usage & GpuTextureUsage.ColorTarget) != 0
            ? new RenderTarget2D(_graphics, info.Width, info.Height)
            : new Texture2D(_graphics, info.Width, info.Height, false, info.Format == GpuTextureFormat.R8Unorm ? SurfaceFormat.Alpha8 : SurfaceFormat.Color);

        return new GpuTexture(body, info.Format, info.Width, info.Height);
    }

    internal GpuSampler CreateSampler(in GpuSamplerInfo info)
    {
        TextureAddressMode address = info.AddressMode switch
        {
            GpuSamplerAddressMode.Repeat => TextureAddressMode.Wrap,
            GpuSamplerAddressMode.MirroredRepeat => TextureAddressMode.Mirror,
            _ => TextureAddressMode.Clamp,
        };

        return new GpuSampler(new SamplerState
        {
            Filter = info.Filter == GpuFilter.Nearest ? TextureFilter.Point : TextureFilter.Linear,
            AddressU = address,
            AddressV = address,
            AddressW = address,
        });
    }

    internal GpuBuffer CreateBuffer(GpuBufferUsage usage, int size) => new(usage, size);

    internal GpuReadback CreateReadback(int size) => new(size);

    internal GpuShader CreateShader(GpuShaderInfo info)
    {
        if (info.Effect is not { } bindings)
        {
            return new GpuShader(info);
        }

        Effect effect = new(_graphics, info.Code.ToArray());
        try
        {
            return new GpuShader(info)
            {
                Effect = effect,
                Uniforms = [.. bindings.Uniforms.Select(uniform => Required(effect, info.Name, uniform.Name))],
                Textures = [.. bindings.Textures.Select(name => name is null ? null : Required(effect, info.Name, name))],
                Sizes = [.. bindings.Sizes.Select(name => name is null ? null : effect.Parameters[name])],
            };
        }
        catch
        {
            effect.Dispose();
            throw;
        }
    }

    // OpenGL compiles and links an effect's program on its first draw, and a pipeline draws one
    // degenerate triangle through it here, which covers no pixel.
    internal GpuPipeline CreatePipeline(in GpuPipelineInfo info)
    {
        GpuBlendState blend = info.Blend;
        GpuPipeline pipeline = new(info, new BlendState
        {
            ColorSourceBlend = Factor(blend.SourceColor),
            ColorDestinationBlend = Factor(blend.DestinationColor),
            ColorBlendFunction = Op(blend.ColorOp),
            AlphaSourceBlend = Factor(blend.SourceAlpha),
            AlphaDestinationBlend = Factor(blend.DestinationAlpha),
            AlphaBlendFunction = Op(blend.AlphaOp),
        });

        ICpuVertexStage stage = CpuVertexStage!;
        if (_degenerate is null)
        {
            _degenerate = new VertexBuffer(_graphics, stage.Declaration, 3, BufferUsage.WriteOnly);
            _degenerate.SetData(new byte[3 * stage.Declaration.VertexStride]);
        }

        _graphics.BlendState = pipeline.Blend;
        info.FragmentShader.Effect!.CurrentTechnique.Passes[0].Apply();
        _graphics.SetVertexBuffer(_degenerate);
        _graphics.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);

        return pipeline;
    }

    // Encodes a completed readback as a PNG with the backend's own encoder, which the committed capture
    // hashes were recorded with.
    internal byte[] EncodePng(GpuReadback readback)
    {
        using Texture2D surface = new(_graphics, readback.Width, readback.Height);
        surface.SetData(readback.Body, 0, readback.Width * readback.Height * 4);
        using MemoryStream encoded = new();
        surface.SaveAsPng(encoded, readback.Width, readback.Height);

        return encoded.ToArray();
    }

    public void Dispose()
    {
        _vertices?.Dispose();
        _degenerate?.Dispose();
    }

    internal GpuTexture? AcquireSwapchainTexture()
    {
        (int width, int height) = SwapchainSize;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        _backBuffer.Width = width;
        _backBuffer.Height = height;

        return _backBuffer;
    }

    internal void Submit()
    {
    }

    internal void UploadToTexture(GpuTexture texture, int x, int y, int width, int height, ReadOnlySpan<byte> texels)
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(texels.Length);
        texels.CopyTo(scratch);
        texture.Body!.SetData(0, new Rectangle(x, y, width, height), scratch, 0, texels.Length);
        ArrayPool<byte>.Shared.Return(scratch);
    }

    // The CPU reads the bytes as they are, and a cycle has nothing to spare.
    internal void UploadToBuffer(GpuBuffer buffer, int offset, ReadOnlySpan<byte> data, bool cycle) =>
        data.CopyTo(buffer.Body.AsSpan(offset));

    internal void DownloadFromTexture(GpuTexture texture, GpuReadback readback)
    {
        readback.Width = texture.Width;
        readback.Height = texture.Height;
        if (texture.Body is { } body)
        {
            body.GetData(readback.Body, 0, texture.Width * texture.Height * 4);
        }
        else
        {
            _graphics.GetBackBufferData(readback.Body);
        }
    }

    internal void BeginRenderPass(in GpuColorTarget target)
    {
        // Binding a target can discard what the back buffer holds, and the back buffer is bound only
        // when a pass leaves another target.
        RenderTarget2D? body = target.Texture.Body as RenderTarget2D;
        if (!ReferenceEquals(body, _bound))
        {
            _graphics.SetRenderTarget(body);
            _bound = body;
        }

        _target = target.Texture;
        _viewport = new Rectangle(0, 0, target.Texture.Width, target.Texture.Height);
        _graphics.Viewport = new Viewport(_viewport);
        _scissor = null;
        _graphics.RasterizerState = Unscissored;
        _graphics.DepthStencilState = DepthStencilState.None;
        _stateChanged = true;
        if (target.Load == GpuLoadOp.Clear)
        {
            _graphics.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, new XnaVector4(target.ClearColor.X, target.ClearColor.Y, target.ClearColor.Z, target.ClearColor.W), 1f, 0);
        }
    }

    internal void EndRenderPass()
    {
        Flush();
        if (_bound is not null)
        {
            _graphics.SetRenderTarget(null);
            _bound = null;
        }

        _target = null;
    }

    internal void SetViewport(int x, int y, int width, int height)
    {
        Rectangle viewport = new(x, y, width, height);
        if (viewport != _viewport)
        {
            Flush();
            _viewport = viewport;
            _graphics.Viewport = new Viewport(viewport);
        }
    }

    // A scissor covering the whole target is none.
    internal void SetScissor(int x, int y, int width, int height)
    {
        Rectangle? scissor = x <= 0 && y <= 0 && x + width >= _target!.Width && y + height >= _target.Height
            ? null
            : new Rectangle(x, y, width, height);
        if (scissor == _scissor)
        {
            return;
        }

        Flush();
        _scissor = scissor;
        _graphics.RasterizerState = scissor is null ? Unscissored : Scissored;
        if (scissor is { } rect)
        {
            _graphics.ScissorRectangle = rect;
        }
    }

    internal void BindGraphicsPipeline(GpuPipeline pipeline)
    {
        _stateChanged |= !ReferenceEquals(pipeline, _pipeline);
        _pipeline = pipeline;
    }

    internal void BindVertexStorageBuffer(int slot, GpuBuffer buffer) => _storage = buffer;

    // Slot 0 holds each draw's own texture. Every other slot is effect state.
    internal void BindFragmentSamplers(int firstSlot, ReadOnlySpan<GpuTextureSamplerBinding> bindings)
    {
        for (int i = 0; i < bindings.Length; i++)
        {
            int slot = firstSlot + i;
            _stateChanged |= slot > 0 && bindings[i] != _samplers[slot];
            _samplers[slot] = bindings[i];
        }
    }

    internal void PushUniformData(GpuShaderStage stage, int slot, ReadOnlySpan<byte> data)
    {
        if (stage == GpuShaderStage.Vertex)
        {
            _stateChanged |= !CpuVertexStage!.SameParameters(_vertexUniforms, data);
            data.CopyTo(_vertexUniforms);
            return;
        }

        if (data.Length != _fragmentLengths[slot] || !data.SequenceEqual(_fragmentUniforms[slot].AsSpan(0, data.Length)))
        {
            if (data.Length > _fragmentUniforms[slot].Length)
            {
                _fragmentUniforms[slot] = new byte[data.Length];
            }

            data.CopyTo(_fragmentUniforms[slot]);
            _fragmentLengths[slot] = data.Length;
            _stateChanged = true;
        }
    }

    internal void DrawPrimitives(int vertexCount, int instanceCount, int firstVertex, int firstInstance)
    {
        if (instanceCount != 1 || firstInstance != 0)
        {
            throw new NotSupportedException("The OpenGL device draws one instance.");
        }

        ICpuVertexStage stage = CpuVertexStage!;
        if (_vertices is null)
        {
            _stride = stage.Declaration.VertexStride;
            _vertices = new DynamicVertexBuffer(_graphics, stage.Declaration, ChunkVertices * RingChunks, BufferUsage.WriteOnly);
            _staging = new byte[ChunkVertices * _stride];
        }

        // An effect that reads the sprite texture's size draws a texture of another size under a state
        // of its own.
        GpuTextureSamplerBinding sprite = _samplers[0];
        if (_pipeline!.Info.FragmentShader.Sizes is [{ }, ..] && _stateCount > 0 && !_stateChanged
            && _states[_stateCount - 1].SpriteSize != (sprite.Texture.Width, sprite.Texture.Height))
        {
            _stateChanged = true;
        }

        while (vertexCount > 0)
        {
            if (_staged == ChunkVertices)
            {
                Flush();
            }

            if (_stateChanged || _stateCount == 0)
            {
                CopyState(sprite.Texture);
            }

            int count = Math.Min(vertexCount, ChunkVertices - _staged);
            stage.Run(_storage!.Body, _vertexUniforms, firstVertex, _staging.AsSpan(_staged * _stride, count * _stride));

            int state = _stateCount - 1;
            if (_runCount > 0 && _runs[_runCount - 1] is { } last && last.Binding == sprite && last.State == state && last.First + last.Count == _staged)
            {
                _runs[_runCount - 1] = last with { Count = last.Count + count };
            }
            else
            {
                _runs[_runCount++] = new Run(sprite, state, _staged, count);
            }

            _staged += count;
            firstVertex += count;
            vertexCount -= count;
        }
    }

    // Copies the effect state the next runs draw under.
    private void CopyState(GpuTexture sprite)
    {
        GpuShader shader = _pipeline!.Info.FragmentShader;
        int samplers = Math.Max(shader.Textures.Length - 1, 0);
        int bytes = UniformBytes + _fragmentLengths[0] + _fragmentLengths[1];
        if (_stateCount == _states.Length)
        {
            Array.Resize(ref _states, _states.Length * 2);
        }

        if (_stateBytesUsed + bytes > _stateBytes.Length)
        {
            Array.Resize(ref _stateBytes, Math.Max(_stateBytes.Length * 2, _stateBytesUsed + bytes));
        }

        if (_stateSamplersUsed + samplers > _stateSamplers.Length)
        {
            Array.Resize(ref _stateSamplers, Math.Max(_stateSamplers.Length * 2, _stateSamplersUsed + samplers));
        }

        State state = new(_pipeline, _stateBytesUsed, _fragmentLengths[0], _fragmentLengths[1], _stateSamplersUsed, (sprite.Width, sprite.Height));
        Span<byte> into = _stateBytes.AsSpan(_stateBytesUsed, bytes);
        _vertexUniforms.CopyTo(into);
        _fragmentUniforms[0].AsSpan(0, state.Fragment0).CopyTo(into[UniformBytes..]);
        _fragmentUniforms[1].AsSpan(0, state.Fragment1).CopyTo(into[(UniformBytes + state.Fragment0)..]);
        _samplers.AsSpan(1, samplers).CopyTo(_stateSamplers.AsSpan(_stateSamplersUsed));
        _stateBytesUsed += bytes;
        _stateSamplersUsed += samplers;
        _states[_stateCount++] = state;
        _stateChanged = false;
    }

    // Uploads the staged vertices, then draws each run, applying its effect state where it differs
    // from the run before.
    private void Flush()
    {
        if (_runCount == 0)
        {
            return;
        }

        SetDataOptions options = SetDataOptions.NoOverwrite;
        if (_cursor + _staged > ChunkVertices * RingChunks)
        {
            _cursor = 0;
            options = SetDataOptions.Discard;
        }

        _vertices!.SetData(_cursor * _stride, _staging, 0, _staged * _stride, 1, options);
        _graphics.SetVertexBuffer(_vertices);

        int applied = -1;
        SamplerState? sampler = null;
        for (int i = 0; i < _runCount; i++)
        {
            Run run = _runs[i];
            if (run.State != applied)
            {
                Apply(in _states[run.State]);
                applied = run.State;
                sampler = null;
            }

            if (!ReferenceEquals(run.Binding.Sampler.Body, sampler))
            {
                sampler = run.Binding.Sampler.Body;
                _graphics.SamplerStates[0] = sampler;
            }

            _graphics.Textures[0] = run.Binding.Texture.Body;
            _graphics.DrawPrimitives(PrimitiveType.TriangleList, _cursor + run.First, run.Count / 3);
        }

        _cursor += _staged;
        _staged = 0;
        _runCount = 0;
        _stateCount = 0;
        _stateBytesUsed = 0;
        _stateSamplersUsed = 0;
        _stateChanged = true;
    }

    private void Apply(in State state)
    {
        GpuShader shader = state.Pipeline.Info.FragmentShader;
        Effect effect = shader.Effect!;
        _graphics.BlendState = state.Pipeline.Blend;

        ReadOnlySpan<byte> bytes = _stateBytes.AsSpan(state.Bytes);
        CpuVertexStage!.SetParameters(effect, bytes[..UniformBytes]);

        GpuEffectUniform[] uniforms = shader.Info.Effect!.Uniforms;
        for (int i = 0; i < uniforms.Length; i++)
        {
            int buffer = UniformBytes + (uniforms[i].Buffer == 0 ? 0 : state.Fragment0);
            ReadOnlySpan<float> value = MemoryMarshal.Cast<byte, float>(bytes.Slice(buffer + uniforms[i].Offset, uniforms[i].Floats * sizeof(float)));
            EffectParameter parameter = shader.Uniforms[i];
            switch (value.Length)
            {
                case 1:
                    parameter.SetValue(value[0]);
                    break;
                case 2:
                    parameter.SetValue(new XnaVector2(value[0], value[1]));
                    break;
                case 3:
                    parameter.SetValue(new XnaVector3(value[0], value[1], value[2]));
                    break;
                default:
                    parameter.SetValue(new XnaVector4(value[0], value[1], value[2], value[3]));
                    break;
            }
        }

        for (int slot = 1; slot < shader.Textures.Length; slot++)
        {
            GpuTextureSamplerBinding binding = _stateSamplers[state.Samplers + slot - 1];
            shader.Textures[slot]!.SetValue(binding.Texture.Body);
            shader.Sizes[slot]?.SetValue(new XnaVector2(binding.Texture.Width, binding.Texture.Height));
            _graphics.SamplerStates[slot] = binding.Sampler.Body;
        }

        if (shader.Sizes is [{ } spriteSize, ..])
        {
            spriteSize.SetValue(new XnaVector2(state.SpriteSize.Width, state.SpriteSize.Height));
        }

        effect.CurrentTechnique.Passes[0].Apply();
    }

    private static EffectParameter Required(Effect effect, string shader, string name) =>
        effect.Parameters[name]
        ?? throw new InvalidOperationException(
            $"Shader '{shader}' shipped without parameter '{name}', which the engine binds. Rebuild the game so the shipped shader and the engine agree.");

    private static Blend Factor(GpuBlendFactor factor) => factor switch
    {
        GpuBlendFactor.Zero => Blend.Zero,
        GpuBlendFactor.One => Blend.One,
        GpuBlendFactor.SourceColor => Blend.SourceColor,
        GpuBlendFactor.OneMinusSourceColor => Blend.InverseSourceColor,
        GpuBlendFactor.DestinationColor => Blend.DestinationColor,
        GpuBlendFactor.OneMinusDestinationColor => Blend.InverseDestinationColor,
        GpuBlendFactor.SourceAlpha => Blend.SourceAlpha,
        GpuBlendFactor.OneMinusSourceAlpha => Blend.InverseSourceAlpha,
        GpuBlendFactor.DestinationAlpha => Blend.DestinationAlpha,
        GpuBlendFactor.OneMinusDestinationAlpha => Blend.InverseDestinationAlpha,
        GpuBlendFactor.ConstantColor => Blend.BlendFactor,
        GpuBlendFactor.OneMinusConstantColor => Blend.InverseBlendFactor,
        _ => Blend.SourceAlphaSaturation,
    };

    private static BlendFunction Op(GpuBlendOp op) => op switch
    {
        GpuBlendOp.Subtract => BlendFunction.Subtract,
        GpuBlendOp.ReverseSubtract => BlendFunction.ReverseSubtract,
        GpuBlendOp.Min => BlendFunction.Min,
        GpuBlendOp.Max => BlendFunction.Max,
        _ => BlendFunction.Add,
    };

    // Vertices First to First + Count of the staged chunk, drawn with Binding at slot 0 under the effect
    // state at index State.
    private readonly record struct Run(GpuTextureSamplerBinding Binding, int State, int First, int Count);

    // A copied effect state: the pipeline, the vertex uniforms at Bytes followed by the two fragment
    // uniform buffers' Fragment0 and Fragment1 bytes, the bindings of sampler slots 1 onwards from
    // Samplers, and the sprite texture's size.
    private readonly record struct State(GpuPipeline Pipeline, int Bytes, int Fragment0, int Fragment1, int Samplers, (int Width, int Height) SpriteSize);
}
