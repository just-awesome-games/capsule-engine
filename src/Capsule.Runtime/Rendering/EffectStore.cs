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

    private static readonly AssetFiles Files = new("shaders", "Shader", "shader");

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
    // transform to clip space. A material's textures sample as the sprite does, through sampler.
    internal void Apply(Material? material, in Matrix transform, SamplerState sampler)
    {
        if (material is null)
        {
            _sprite.Transform.SetValue(transform);
            _sprite.Pass.Apply();
            return;
        }

        Binding binding = Get(material.Shader);
        binding.Transform.SetValue(transform);

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
                    parameter.SetValue(material.TryGetTexture(i, out TextureHandle texture) ? WholeTexture!(texture) : null);
                    break;
            }
        }

        binding.Pass.Apply();

        // The build binds a shader's textures from slot 1 in table order, the sprite's at 0, and the
        // shader carries no sampler state of its own.
        for (int slot = 1; slot <= binding.Textures; slot++)
        {
            _device.SamplerStates[slot] = sampler;
        }
    }

    private Binding Get(Shader shader)
    {
        if (_loaded.TryGetValue(shader, out Binding? binding))
        {
            return binding;
        }

        binding = Load(shader);
        _loaded.Add(shader, binding);
        Log.Info($"shader '{shader.Name}' loaded on first draw. Hold its material from construction to preload it");

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

            ReadOnlySpan<ShaderParameter> table = shader is null ? default : shader.Parameters;
            Parameters = new EffectParameter[table.Length];

            for (int i = 0; i < table.Length; i++)
            {
                Parameters[i] = effect.Parameters[table[i].Name]
                    ?? throw new InvalidOperationException(
                        $"Shader '{shader!.Name}' shipped without parameter '{table[i].Name}', which its generated key declares. Rebuild the game so the shipped shader and the code agree.");

                if (table[i].Kind == ShaderParameterKind.Texture)
                {
                    Textures++;
                }
            }
        }

        internal Effect Effect { get; }

        internal EffectPass Pass { get; }

        internal EffectParameter Transform { get; }

        internal EffectParameter[] Parameters { get; }

        // How many texture slots beyond the sprite's the shader samples.
        internal int Textures { get; }
    }
}
