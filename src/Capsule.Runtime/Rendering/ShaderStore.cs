using System.Buffers.Binary;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.Rendering;

// The shaders sprites draw with, and a pipeline for each blend, surface format and form they draw with into the
// renderer's surfaces: Capsule's own sprite shader, which alone draws the light, and every game shader, loaded
// with its pipelines at the scene boundary that first holds it. Each is compiled lit and unlit, and a draw that
// binds no light takes the unlit form, which reads none. A draw waits on no driver's compile, except that a
// shader no boundary collected loads on its first draw and logs it. Each is kept for the run.
// Binding a material pushes its values, the scene's globals and where each of its textures sits on the
// texture bound for it, through offsets resolved at load. A frame at steady state looks nothing up by
// name and allocates nothing.
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

    private const string EngineResources = "Capsule.Runtime.Rendering.Shaders.";

    private static readonly AssetFiles Files = new("Shader", "shader");

    private readonly GpuDevice _device;
    private readonly HostPlatform _platform;
    private readonly GpuTextureFormat _target;
    private readonly GpuShader _vertex;
    private readonly Binding _sprite;

    // Keyed by reference: each generated shader is one instance for the run.
    private readonly Dictionary<Shader, Binding> _loaded = [];

    // The sampler slot of a material's first texture, after the sprite's and the light's, which the draw
    // list binds.
    private const int FirstMaterialSlot = 2;

    // The material's texture bindings, FirstMaterialSlot onwards, written in place for each bind.
    private readonly GpuTextureSamplerBinding[] _textures = new GpuTextureSamplerBinding[14];

    // The fragment's uniform buffer of texture regions, after the material's, the scene globals' and
    // the picture's.
    private const int RegionsSlot = 3;

    // A texture's region and its size in texels, each padded to four floats.
    private const int RegionBytes = 8 * sizeof(float);

    // Resolves a texture a material reads, or the default handle of one it never set. The frame
    // renderer sets it, since it owns the engine's textures and the scene's.
    internal Func<TextureHandle, MaterialTexture>? MaterialTexture { get; set; }

    internal ShaderGlobals Globals { get; set; } = new();

    // target: the format of every surface a sprite is drawn into but the light, which each pipeline targets.
    internal ShaderStore(GpuDevice device, HostPlatform platform, GpuTextureFormat target)
    {
        _device = device;
        _platform = platform;
        _target = target;
        _vertex = CreateShader(ReadEngine("vertex", device.ShaderFormat), "vertex", GpuShaderStage.Vertex);

        ShaderContainer sprite = ReadEngine("sprite", device.ShaderFormat);
        _sprite = Bind(sprite, "sprite", null);
        CreatePipeline(_sprite, Premultiplied, target, lit: true);
        CreatePipeline(_sprite, Premultiplied, target, lit: false);
        CreatePipeline(_sprite, Premultiplied, FrameRenderer.LightFormat, lit: false);
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

    // The pipeline material's shader, or Capsule's own for null, draws through with blend into a surface of format,
    // in its lit form for a draw that binds a light and its unlit form otherwise.
    internal GpuPipeline Pipeline(Material? material, in GpuBlendState blend, GpuTextureFormat format, bool lit)
    {
        Binding binding = material is null ? _sprite : Get(material.Shader);
        lit |= binding.OneForm;
        if (!binding.Pipelines.TryGetValue((blend, format, lit), out GpuPipeline? pipeline))
        {
            pipeline = CreatePipeline(binding, blend, format, lit);
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
                MaterialTexture!(material.TryGetTexture(i, out TextureHandle texture) ? texture : default);
            }
        }
    }

    // Pushes material's values and the scene's globals, and binds its textures from FirstMaterialSlot, each through
    // its own sampling's sampler or else through sampler, with the region each fills of what is bound for
    // it. After them comes screen, if the shader reads the picture, or the transparent texel where nothing
    // was copied. Capsule's own shader reads none.
    internal void BindMaterial(GpuCommandBuffer commands, GpuRenderPass pass, Material? material, GpuSampler sampler, SampledTexture? screen)
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
                MaterialTexture read = MaterialTexture!(material.TryGetTexture(i, out TextureHandle texture) ? texture : default);
                int slot = binding.Locations[i] - FirstMaterialSlot;
                _textures[slot] = new GpuTextureSamplerBinding(read.Texture.Texture, read.Texture.Sampler ?? sampler);
                Write(binding.Regions, slot * RegionBytes, ShaderParameterKind.Vector4, read.Region);
                Write(binding.Regions, (slot * RegionBytes) + (RegionBytes / 2), ShaderParameterKind.Vector2, new System.Numerics.Vector4(read.Texels, 0f, 0f));
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

        if (binding.Regions.Length > 0)
        {
            commands.PushFragmentUniformData(RegionsSlot, binding.Regions);
        }

        if (material.Shader.ReadsScreen)
        {
            _textures[textures++] = new GpuTextureSamplerBinding((screen ?? MaterialTexture!(default).Texture).Texture, sampler);
        }

        pass.BindFragmentSamplers(FirstMaterialSlot, _textures.AsSpan(0, textures));
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

        Binding binding = Bind(container, shader.Name, shader);
        CreatePipeline(binding, Premultiplied, _target, lit: true);
        if (!binding.OneForm)
        {
            CreatePipeline(binding, Premultiplied, _target, lit: false);
        }

        return binding;
    }

    // A fragment's binding with both its forms, or its one form twice where the container holds no unlit code.
    private Binding Bind(ShaderContainer container, string name, Shader? source)
    {
        GpuShader lit = CreateShader(container, name, GpuShaderStage.Fragment);
        GpuShader unlit = container.UnlitCode.Length == 0 ? lit : CreateShader(container, name + " (unlit)", GpuShaderStage.Fragment, container.UnlitCode);

        return new Binding(lit, unlit, source, container);
    }

    private GpuShader CreateShader(ShaderContainer container, string name, GpuShaderStage stage, byte[]? code = null) =>
        _device.CreateShader(new GpuShaderInfo(
            name,
            code ?? container.Code,
            container.EntryPoint,
            stage,
            container.Samplers,
            container.StorageTextures,
            container.StorageBuffers,
            container.UniformBuffers));

    private GpuPipeline CreatePipeline(Binding binding, in GpuBlendState blend, GpuTextureFormat format, bool lit)
    {
        GpuPipeline pipeline = _device.CreatePipeline(new GpuPipelineInfo(_vertex, lit ? binding.Shader : binding.Unlit, format, blend));
        binding.Pipelines.Add((blend, format, lit), pipeline);

        return pipeline;
    }

    // One loaded shader, its pipelines, and where each of its parameters sits: a byte offset into its
    // uniform buffer, or a sampler slot. Several materials share one binding, and each bind writes every
    // value into the buffers it pushes. The scene globals it reads are resolved to indices into a
    // scene's values once, and again when the scene or its set of names changes.
    private sealed class Binding : IDisposable
    {
        // The lit form, which multiplies by the light, and the unlit form, the same shader where there is one form.
        internal GpuShader Shader { get; }

        internal GpuShader Unlit { get; }

        // Whether the shader reads the picture and was compiled in one form, which every draw takes.
        internal bool OneForm => ReferenceEquals(Shader, Unlit);

        internal string Name { get; }

        internal Dictionary<(GpuBlendState Blend, GpuTextureFormat Format, bool Lit), GpuPipeline> Pipelines { get; } = [];

        // Null for the engine's own shader, which declares none.
        internal GlobalSlots? Globals { get; }

        // By parameter in the shader's table order: a byte offset into its buffer, or a sampler slot.
        internal int[] Locations { get; }

        internal byte[] Material { get; }

        internal byte[] SceneGlobals { get; }

        // Each texture's region and size, in sampler slot order.
        internal byte[] Regions { get; }

        internal Binding(GpuShader shader, GpuShader unlit, Shader? source, ShaderContainer container)
        {
            Shader = shader;
            Unlit = unlit;
            Name = source?.Name ?? "sprite";
            Globals = source is null ? null : new GlobalSlots(source);

            ReadOnlySpan<ShaderParameter> table = source is null ? default : source.Parameters;
            Locations = new int[table.Length];
            int material = 0;
            int globals = 0;
            int textures = 0;
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
                if (shipped.Kind == ShaderParameterKind.Texture)
                {
                    textures = Math.Max(textures, shipped.Location - FirstMaterialSlot + 1);
                }
                else
                {
                    int end = shipped.Location + (Floats(shipped.Kind) * sizeof(float));
                    material = shipped.Global ? material : Math.Max(material, end);
                    globals = shipped.Global ? Math.Max(globals, end) : globals;
                }
            }

            Material = new byte[(material + 15) & ~15];
            SceneGlobals = new byte[(globals + 15) & ~15];
            Regions = new byte[textures * RegionBytes];
        }

        public void Dispose()
        {
            foreach (GpuPipeline pipeline in Pipelines.Values)
            {
                pipeline.Dispose();
            }

            Shader.Dispose();
            if (!OneForm)
            {
                Unlit.Dispose();
            }
        }
    }
}
