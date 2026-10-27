using System.Buffers.Binary;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// The shaders sprites draw with, and a pipeline for each blend they draw with into the renderer's
// surfaces: Capsule's own sprite shader, and every game shader, loaded with its pipelines at the scene
// boundary that first holds it. A draw waits on no driver's compile, except that a shader no boundary
// collected loads on its first draw and logs it. Each is kept for the run.
// Binding a material pushes its values and the scene's globals through offsets resolved at load. A
// frame at steady state looks nothing up by name and allocates nothing.
internal sealed class ShaderStore : IDisposable
{
    // Premultiplied alpha: source + destination x (1 - source alpha).
    internal static readonly GpuBlendState Premultiplied = new(
        GpuBlendFactor.One,
        GpuBlendFactor.OneMinusSourceAlpha,
        GpuBlendOp.Add,
        GpuBlendFactor.One,
        GpuBlendFactor.OneMinusSourceAlpha,
        GpuBlendOp.Add);

    // The world's colour times twice the light map: source x destination + destination x source, alpha
    // kept. Both factors stay inside [0, 1], where every desktop API blends exactly.
    internal static readonly GpuBlendState Modulate2x = new(
        GpuBlendFactor.DestinationColor,
        GpuBlendFactor.SourceColor,
        GpuBlendOp.Add,
        GpuBlendFactor.Zero,
        GpuBlendFactor.One,
        GpuBlendOp.Add);

    private const string EngineResources = "Capsule.Runtime.Rendering.Shaders.";

    private static readonly AssetFiles Files = new("Shader", "shader");

    private readonly GpuDevice _device;
    private readonly HostPlatform _platform;
    private readonly GpuTextureFormat _target;
    private readonly GpuShader _vertex;
    private readonly Binding _sprite;

    // Keyed by reference: each generated shader is one instance for the run.
    private readonly Dictionary<Shader, Binding> _loaded = [];

    // The material's texture bindings, slot 1 onwards, written in place for each bind.
    private readonly GpuTextureSamplerBinding[] _textures = new GpuTextureSamplerBinding[15];

    // Resolves a texture a material binds whole, or the default handle of one it never set. The frame
    // renderer sets it, since it owns the engine's textures and the scene's.
    internal Func<TextureHandle, SampledTexture>? WholeTexture { get; set; }

    internal ShaderGlobals Globals { get; set; } = new();

    // target: the format of every surface a sprite is drawn into, which each pipeline targets.
    internal ShaderStore(GpuDevice device, HostPlatform platform, GpuTextureFormat target)
    {
        _device = device;
        _platform = platform;
        _target = target;
        _vertex = CreateShader(ReadEngine("vertex", device.ShaderFormat), "vertex", GpuShaderStage.Vertex);

        ShaderContainer sprite = ReadEngine("sprite", device.ShaderFormat);
        _sprite = new Binding(CreateShader(sprite, "sprite", GpuShaderStage.Fragment), null, sprite);
        CreatePipeline(_sprite, Premultiplied);
        CreatePipeline(_sprite, Modulate2x);
    }

    // Loads the shaders a scene boundary collected that no earlier scene loaded, with their pipelines.
    internal void Load(IReadOnlyList<Shader> shaders)
    {
        foreach (Shader shader in shaders)
        {
            if (!_loaded.ContainsKey(shader))
            {
                _loaded.Add(shader, Load(shader));
            }
        }
    }

    // The pipeline material's shader, or Capsule's own for null, draws through with blend.
    internal GpuPipeline Pipeline(Material? material, in GpuBlendState blend)
    {
        Binding binding = material is null ? _sprite : Get(material.Shader);
        if (!binding.Pipelines.TryGetValue(blend, out GpuPipeline? pipeline))
        {
            pipeline = CreatePipeline(binding, blend);
            Log.Info($"shader '{binding.Name}' compiled a pipeline on first draw");
        }

        return pipeline;
    }

    // Loads each texture material binds while the frame records. Its upload then joins the frame's copy
    // pass.
    internal void Prepare(Material material)
    {
        ReadOnlySpan<ShaderParameter> parameters = material.Shader.Parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Kind == ShaderParameterKind.Texture)
            {
                WholeTexture!(material.TryGetTexture(i, out TextureHandle texture) ? texture : default);
            }
        }
    }

    // Pushes material's values and the scene's globals, and binds its textures from slot 1, each through
    // its own sampling's sampler or else through sampler. Capsule's own shader reads none.
    internal void BindMaterial(GpuCommandBuffer commands, GpuRenderPass pass, Material? material, GpuSampler sampler)
    {
        if (material is null)
        {
            return;
        }

        Binding binding = Get(material.Shader);
        ReadOnlySpan<ShaderParameter> parameters = material.Shader.Parameters;
        int textures = 0;
        for (int i = 0; i < parameters.Length; i++)
        {
            ShaderParameter parameter = parameters[i];
            if (parameter.Kind == ShaderParameterKind.Texture)
            {
                SampledTexture bound = WholeTexture!(material.TryGetTexture(i, out TextureHandle texture) ? texture : default);
                _textures[binding.Locations[i] - 1] = new GpuTextureSamplerBinding(bound.Texture, bound.Sampler ?? sampler);
                textures++;
            }
            else if (!parameter.Global)
            {
                Write(binding.Material, binding.Locations[i], parameter.Kind, material.Value(i));
            }
        }

        if (binding.Globals is { Count: > 0 } slots)
        {
            ReadOnlySpan<int> sources = slots.Resolve(Globals);
            for (int g = 0; g < sources.Length; g++)
            {
                Write(binding.SceneGlobals, binding.Locations[slots.Parameter(g)], slots.Kind(g), sources[g] < 0 ? default : Globals.ValueAt(sources[g]));
            }
        }

        if (binding.Material.Length > 0)
        {
            commands.PushFragmentUniformData(0, binding.Material);
        }

        if (binding.SceneGlobals.Length > 0)
        {
            commands.PushFragmentUniformData(1, binding.SceneGlobals);
        }

        pass.BindFragmentSamplers(1, _textures.AsSpan(0, textures));
    }

    // The shipped container of a game shader, as a content path. The format joins the name, since an
    // asset's extension is one segment.
    internal static string RelativePathOf(Shader shader, string format) => Files.RelativePathOf(shader.Name + "." + format, ShaderContainer.Extension);

    public void Dispose()
    {
        _vertex.Dispose();
        _sprite.Dispose();
        foreach (Binding binding in _loaded.Values)
        {
            binding.Dispose();
        }
    }

    private static void Write(byte[] buffer, int offset, ShaderParameterKind kind, System.Numerics.Vector4 value)
    {
        Span<byte> at = buffer.AsSpan(offset);
        BinaryPrimitives.WriteSingleLittleEndian(at, value.X);
        if (kind is ShaderParameterKind.Vector2 or ShaderParameterKind.Vector3 or ShaderParameterKind.Vector4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(at[4..], value.Y);
        }

        if (kind is ShaderParameterKind.Vector3 or ShaderParameterKind.Vector4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(at[8..], value.Z);
        }

        if (kind is ShaderParameterKind.Vector4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(at[12..], value.W);
        }
    }

    private static int Floats(ShaderParameterKind kind) => kind switch
    {
        ShaderParameterKind.Float => 1,
        ShaderParameterKind.Vector2 => 2,
        ShaderParameterKind.Vector3 => 3,
        _ => 4,
    };

    // One of the engine's own stages, embedded in every format the engine compiles.
    private static ShaderContainer ReadEngine(string stage, string format)
    {
        string resource = EngineResources + stage + ShaderContainer.Suffix(format);
        using Stream stream = typeof(ShaderStore).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The engine's {stage} shader is not embedded in the {format} format this GPU reads. A native publish keeps only the formats its runtime identifier reads. Publish for the platform the game runs on.");

        return Read(stream, stage);
    }

    private static ShaderContainer Read(Stream stream, string name)
    {
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);

        return ShaderContainer.Read(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), name);
    }

    private Binding Get(Shader shader)
    {
        if (_loaded.TryGetValue(shader, out Binding? binding))
        {
            return binding;
        }

        binding = Load(shader);
        _loaded.Add(shader, binding);
        Log.Info($"shader '{shader.Name}' loaded on first draw. Hold its material from construction, or add it in CollectAssets, to preload it");

        return binding;
    }

    private Binding Load(Shader shader)
    {
        ShaderContainer container;
        using (Stream file = Files.Open(_platform, shader.Name + "." + _device.ShaderFormat, ShaderContainer.Extension))
        {
            container = Read(file, shader.Name);
        }

        Binding binding = new(CreateShader(container, shader.Name, GpuShaderStage.Fragment), shader, container);
        CreatePipeline(binding, Premultiplied);

        return binding;
    }

    private GpuShader CreateShader(ShaderContainer container, string name, GpuShaderStage stage) =>
        _device.CreateShader(new GpuShaderInfo(
            name,
            container.Code,
            container.EntryPoint,
            stage,
            container.Samplers,
            container.StorageTextures,
            container.StorageBuffers,
            container.UniformBuffers));

    private GpuPipeline CreatePipeline(Binding binding, in GpuBlendState blend)
    {
        GpuPipeline pipeline = _device.CreatePipeline(new GpuPipelineInfo(_vertex, binding.Shader, _target, blend));
        binding.Pipelines.Add(blend, pipeline);

        return pipeline;
    }

    // One loaded shader, its pipelines, and where each of its parameters sits: a byte offset into its
    // uniform buffer, or a sampler slot. Several materials share one binding, and each bind writes every
    // value into the two buffers it pushes. The scene globals it reads are resolved to indices into a
    // scene's values once, and again when the scene or its set of names changes.
    private sealed class Binding : IDisposable
    {
        internal GpuShader Shader { get; }

        internal string Name { get; }

        internal Dictionary<GpuBlendState, GpuPipeline> Pipelines { get; } = [];

        // Null for the engine's own shader, which declares none.
        internal GlobalSlots? Globals { get; }

        // By parameter in the shader's table order: a byte offset into its buffer, or a sampler slot.
        internal int[] Locations { get; }

        internal byte[] Material { get; }

        internal byte[] SceneGlobals { get; }

        internal Binding(GpuShader shader, Shader? source, ShaderContainer container)
        {
            Shader = shader;
            Name = source?.Name ?? "sprite";
            Globals = source is null ? null : new GlobalSlots(source);

            ReadOnlySpan<ShaderParameter> table = source is null ? default : source.Parameters;
            Locations = new int[table.Length];
            int material = 0;
            int globals = 0;
            for (int i = 0; i < table.Length; i++)
            {
                string name = table[i].Name;
                ShaderBinding shipped = Array.Find(container.Parameters, parameter => parameter.Name == name);
                if (shipped.Name is null || shipped.Kind != table[i].Kind || shipped.Global != table[i].Global)
                {
                    throw new InvalidOperationException(
                        $"Shader '{Name}' shipped without parameter '{table[i].Name}' as its generated key declares it. Rebuild the game so the shipped shader and the code agree.");
                }

                Locations[i] = shipped.Location;
                if (shipped.Kind != ShaderParameterKind.Texture)
                {
                    int end = shipped.Location + (Floats(shipped.Kind) * sizeof(float));
                    material = shipped.Global ? material : Math.Max(material, end);
                    globals = shipped.Global ? Math.Max(globals, end) : globals;
                }
            }

            Material = new byte[(material + 15) & ~15];
            SceneGlobals = new byte[(globals + 15) & ~15];
        }

        public void Dispose()
        {
            foreach (GpuPipeline pipeline in Pipelines.Values)
            {
                pipeline.Dispose();
            }

            Shader.Dispose();
        }
    }
}
