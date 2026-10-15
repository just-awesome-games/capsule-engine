using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>A shader and the values of its parameters, drawn by every renderer that holds it.</summary>
/// <remarks>
/// Neighbouring renderers draw in one batch when they hold the same instance and the same texture,
/// so share one material across the renderers that look alike. A parameter never set reads zero,
/// whatever the shader source initializes it to. A texture set here is bound whole and must not be
/// packed into an atlas. <c>Set</c> throws when the shader declares no parameter of that name, or
/// declares it as another kind, and the message lists what it declares. It also throws for a
/// parameter the shader declares in its <c>SceneGlobals</c> block, which the scene sets for every
/// material.
/// </remarks>
/// <example>
/// <code>
/// private static readonly Material Stone = new(CapsuleAssets.Shaders.DesaturateShader);
///
/// Stone.Set("Amount", 1f);
/// sprite.Material = Stone;
/// </code>
/// </example>
public sealed class Material
{
    private readonly Vector4[] _values;
    private readonly TextureHandle[] _textures;

    /// <summary>A material drawing with <paramref name="shader"/>, with no parameter set.</summary>
    public Material(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        Shader = shader;
        int count = shader.Parameters.Length;
        _values = new Vector4[count];
        _textures = new TextureHandle[count];
    }

    /// <summary>The shader this material draws with.</summary>
    public Shader Shader { get; }

    /// <summary>Sets the <c>float</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, float value)
    {
        Guard.Finite(value, nameof(value));
        _values[Slot(name, ShaderParameterKind.Float)] = new Vector4(value, 0f, 0f, 0f);
    }

    /// <summary>Sets the <c>float2</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector2 value)
    {
        Guard.Finite(value, nameof(value));
        _values[Slot(name, ShaderParameterKind.Vector2)] = new Vector4(value, 0f, 0f);
    }

    /// <summary>Sets the <c>float3</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector3 value)
    {
        Vector4 widened = new(value, 0f);
        Guard.Finite(widened, nameof(value));
        _values[Slot(name, ShaderParameterKind.Vector3)] = widened;
    }

    /// <summary>Sets the <c>float4</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector4 value)
    {
        Guard.Finite(value, nameof(value));
        _values[Slot(name, ShaderParameterKind.Vector4)] = value;
    }

    /// <summary>
    /// Sets the <c>float4</c> parameter <paramref name="name"/> to <paramref name="value"/>, straight
    /// alpha, each channel from 0 to 1.
    /// </summary>
    public void Set(string name, ColorRgba value) =>
        _values[Slot(name, ShaderParameterKind.Vector4)] = VectorOf(value);

    /// <summary>
    /// Sets the <c>Texture2D</c> parameter <paramref name="name"/>, which the shader reads with the
    /// texture's own sampling or else the frame's, clamped at its edges.
    /// </summary>
    /// <remarks>
    /// The texture is bound whole, and drawing throws when the build packed it into an atlas. Set
    /// <c>"atlas": false</c> in its config.
    /// An r8 texture's value is in <c>.r</c>.
    /// </remarks>
    public void Set(string name, TextureHandle value)
    {
        if (value.Name is null || value.Extension is null)
        {
            throw new ArgumentException("A default TextureHandle names no texture. Pass a texture's CapsuleAssets member.", nameof(value));
        }

        _textures[Slot(name, ShaderParameterKind.Texture)] = value;
    }

    /// <summary>Reads the <c>float</c> parameter <paramref name="name"/>, zero until it is set.</summary>
    public float GetFloat(string name) => _values[Slot(name, ShaderParameterKind.Float)].X;

    /// <summary>Reads the <c>float2</c> parameter <paramref name="name"/>, zero until it is set.</summary>
    public Vector2 GetVector2(string name)
    {
        Vector4 value = _values[Slot(name, ShaderParameterKind.Vector2)];

        return new Vector2(value.X, value.Y);
    }

    /// <summary>Reads the <c>float3</c> parameter <paramref name="name"/>, zero until it is set.</summary>
    public Vector3 GetVector3(string name)
    {
        Vector4 value = _values[Slot(name, ShaderParameterKind.Vector3)];

        return new Vector3(value.X, value.Y, value.Z);
    }

    /// <summary>Reads the <c>float4</c> parameter <paramref name="name"/>, zero until it is set.</summary>
    public Vector4 GetVector4(string name) => _values[Slot(name, ShaderParameterKind.Vector4)];

    /// <summary>
    /// Reads the <c>float4</c> parameter <paramref name="name"/> as a straight-alpha colour, each channel
    /// clamped to 0 to 1 and rounded to the nearest byte. Transparent black until it is set.
    /// </summary>
    public ColorRgba GetColor(string name) => ColorOf(_values[Slot(name, ShaderParameterKind.Vector4)]);

    /// <summary>
    /// Reads the <c>Texture2D</c> parameter <paramref name="name"/>. A texture never set reads as a
    /// default handle, whose <c>Name</c> is null.
    /// </summary>
    public TextureHandle GetTexture(string name) => _textures[Slot(name, ShaderParameterKind.Texture)];

    // The value of parameter index, zero until set. A vector of fewer than four components fills the
    // rest with zero.
    internal Vector4 Value(int index) => _values[index];

    // The texture set on parameter index, or false when none was. Set refuses a default handle.
    internal bool TryGetTexture(int index, out TextureHandle texture) => (texture = _textures[index]).Name is not null;

    // The float4 Set(ColorRgba) stores for a colour: straight alpha, each channel from 0 to 1.
    internal static Vector4 VectorOf(ColorRgba value) => new Vector4(value.R, value.G, value.B, value.A) / 255f;

    // The straight-alpha colour of a float4 holding 0 to 1 channels, the inverse of VectorOf.
    internal static ColorRgba ColorOf(Vector4 value)
    {
        static byte Channel(float channel) => (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);

        return new ColorRgba(Channel(value.X), Channel(value.Y), Channel(value.Z), Channel(value.W));
    }

    // The index of parameter name when it is a material parameter of the given kind.
    private int Slot(string name, ShaderParameterKind kind)
    {
        ArgumentNullException.ThrowIfNull(name);

        int index = Shader.IndexOf(name);
        if (index < 0)
        {
            throw new ArgumentException(
                $"Shader '{Shader.Name}' declares no parameter '{name}'. It declares {Declared()}.",
                nameof(name));
        }

        if (Shader.Parameters[index].Global)
        {
            throw new ArgumentException(
                $"Shader '{Shader.Name}' declares '{name}' in its {ShaderGlobals.Block} block, so a material neither sets nor reads it. Use scene.ShaderGlobals.Set or the Get of its kind.",
                nameof(name));
        }

        ShaderParameterKind declared = Shader.Parameters[index].Kind;
        if (declared != kind)
        {
            throw new ArgumentException(
                $"Shader '{Shader.Name}' declares '{name}' as a {Spelling(declared)}. Pass it {Argument(declared)}, or read it with {Reading(declared)}.",
                nameof(name));
        }

        return index;
    }

    private string Declared()
    {
        ReadOnlySpan<ShaderParameter> parameters = Shader.Parameters;
        List<string> names = [];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (!parameters[i].Global)
            {
                names.Add($"'{parameters[i].Name}' ({Spelling(parameters[i].Kind)})");
            }
        }

        return names.Count == 0 ? "no parameters" : string.Join(", ", names);
    }

    internal static string Spelling(ShaderParameterKind kind) => kind switch
    {
        ShaderParameterKind.Float => "float",
        ShaderParameterKind.Vector2 => "float2",
        ShaderParameterKind.Vector3 => "float3",
        ShaderParameterKind.Vector4 => "float4",
        _ => "texture",
    };

    internal static string Reading(ShaderParameterKind kind) => kind switch
    {
        ShaderParameterKind.Float => "GetFloat",
        ShaderParameterKind.Vector2 => "GetVector2",
        ShaderParameterKind.Vector3 => "GetVector3",
        ShaderParameterKind.Vector4 => "GetVector4 or GetColor",
        _ => "GetTexture",
    };

    internal static string Argument(ShaderParameterKind kind) => kind switch
    {
        ShaderParameterKind.Float => "a float",
        ShaderParameterKind.Vector2 => "a Vector2",
        ShaderParameterKind.Vector3 => "a Vector3",
        ShaderParameterKind.Vector4 => "a Vector4 or a ColorRgba",
        _ => "a TextureHandle",
    };
}
