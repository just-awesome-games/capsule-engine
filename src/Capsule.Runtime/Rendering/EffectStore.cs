using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// The effects sprites draw with: Capsule's own sprite shader, and every game shader, loaded at the scene
// boundary that first holds it or on its first draw, and kept for the run. Applying a material sets
// the transform and every parameter through handles resolved at load, so a frame at steady state
// looks nothing up by name and allocates nothing.
internal sealed class EffectStore : IDisposable
{
    private const string SpriteShaderResource = "Capsule.Runtime.Rendering.Shaders.sprite.mgfx";

    // The template's coverage switch, which every shader the build composes carries.
    private const string CoverageParameter = "CapsuleCoverage";

    // The template's size parameters, completed by texture slot, the sprite's being 0. A shader carries
    // one only for a texture whose size it reads.
    private const string SizeParameter = "CapsuleTextureSize";

    private static readonly AssetFiles Files = new("Shader", "shader");

    private readonly GraphicsDevice _device;
    private readonly HostPlatform _platform;
    private readonly Binding _sprite;

    // Keyed by reference: each generated shader is one instance for the run.
    private readonly Dictionary<Shader, Binding> _loaded = [];

    internal EffectStore(GraphicsDevice device, HostPlatform platform)
    {
        _device = device;
        _platform = platform;

        using Stream resource = typeof(EffectStore).Assembly.GetManifestResourceStream(SpriteShaderResource)
            ?? throw new InvalidOperationException($"The embedded sprite shader '{SpriteShaderResource}' is missing.");
        _sprite = new Binding(new Effect(device, ReadAll(resource)), null);
    }

    // Resolves a texture a material binds whole. The frame renderer sets it, since it owns the
    // engine's textures and the scene's.
    internal Func<TextureHandle, Texture2D>? WholeTexture { get; set; }

    // Loads the shaders a scene boundary collected that no earlier scene loaded.
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

    // Applies material's shader, or Capsule's own for null, with transform as the geometry's full
    // transform to clip space. Coverage is whether the sprite texture is single-channel, and
    // spriteSize its size in texels. A material's texture samples through its own sampling's state, or
    // through sampler when it has none. Returns whether the shader reads the sprite texture's size,
    // which a run on a texture of another size must then apply again.
    internal bool Apply(Material? material, in Matrix transform, SamplerState sampler, bool coverage, Point spriteSize)
    {
        if (material is null)
        {
            _sprite.Transform.SetValue(transform);
            _sprite.Coverage.SetValue(coverage ? 1f : 0f);
            _sprite.Pass.Apply();
            return false;
        }

        Binding binding = Get(material.Shader);
        binding.Transform.SetValue(transform);
        binding.Coverage.SetValue(coverage ? 1f : 0f);
        binding.Sizes[0]?.SetValue(spriteSize.ToVector2());
        int slot = 1;

        ReadOnlySpan<ShaderParameter> parameters = material.Shader.Parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            EffectParameter parameter = binding.Parameters[i];
            System.Numerics.Vector4 value = material.Value(i);
            switch (parameters[i].Kind)
            {
                case ShaderParameterKind.Float:
                    parameter.SetValue(value.X);
                    break;
                case ShaderParameterKind.Vector2:
                    parameter.SetValue(new Vector2(value.X, value.Y));
                    break;
                case ShaderParameterKind.Vector3:
                    parameter.SetValue(new Vector3(value.X, value.Y, value.Z));
                    break;
                case ShaderParameterKind.Vector4:
                    parameter.SetValue(new Vector4(value.X, value.Y, value.Z, value.W));
                    break;
                default:
                    // The build binds a shader's textures from slot 1 in table order, the sprite's at 0,
                    // and the shader carries no sampler state of its own.
                    Texture2D? bound = material.TryGetTexture(i, out TextureHandle texture) ? WholeTexture!(texture) : null;
                    parameter.SetValue(bound);
                    binding.Sizes[slot]?.SetValue(bound is null ? Vector2.Zero : new Vector2(bound.Width, bound.Height));
                    _device.SamplerStates[slot++] = bound?.Tag as SamplerState ?? sampler;
                    break;
            }
        }

        binding.Pass.Apply();
        return binding.Sizes[0] is not null;
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
        using Stream file = Files.Open(_platform, shader.Name, ".mgfx");

        return new Binding(new Effect(_device, ReadAll(file)), shader);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);

        return bytes.ToArray();
    }

    public void Dispose()
    {
        _sprite.Effect.Dispose();
        foreach (Binding binding in _loaded.Values)
        {
            binding.Effect.Dispose();
        }
    }

    // One loaded effect and its parameters in the shader's table order. Several materials share one
    // effect, so each application writes every parameter.
    private sealed class Binding
    {
        internal Binding(Effect effect, Shader? shader)
        {
            Effect = effect;
            Pass = effect.CurrentTechnique.Passes[0];
            Transform = effect.Parameters["MatrixTransform"];
            Coverage = effect.Parameters[CoverageParameter]
                ?? throw new InvalidOperationException(
                    $"Shader '{shader?.Name ?? "sprite"}' shipped without the engine's coverage parameter. Rebuild the game so the shipped shader and the engine agree.");

            ReadOnlySpan<ShaderParameter> table = shader is null ? default : shader.Parameters;
            Parameters = new EffectParameter[table.Length];
            int textures = 0;

            for (int i = 0; i < table.Length; i++)
            {
                Parameters[i] = effect.Parameters[table[i].Name]
                    ?? throw new InvalidOperationException(
                        $"Shader '{shader!.Name}' shipped without parameter '{table[i].Name}', which its generated key declares. Rebuild the game so the shipped shader and the code agree.");
                textures += table[i].Kind == ShaderParameterKind.Texture ? 1 : 0;
            }

            Sizes = new EffectParameter?[textures + 1];
            for (int slot = 0; slot < Sizes.Length; slot++)
            {
                Sizes[slot] = effect.Parameters[SizeParameter + slot.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            }
        }

        internal Effect Effect { get; }

        internal EffectPass Pass { get; }

        internal EffectParameter Transform { get; }

        internal EffectParameter Coverage { get; }

        internal EffectParameter[] Parameters { get; }

        // By texture slot, null where the shader never reads that texture's size.
        internal EffectParameter?[] Sizes { get; }
    }
}
