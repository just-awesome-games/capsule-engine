using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>A shader and the values of its parameters, drawn by every renderer that holds it.</summary>
/// <remarks>
/// Neighbouring renderers draw in one batch when they hold the same instance and the same texture.
/// Share one material across the renderers that look alike. A parameter never set reads zero,
/// whatever the shader source initializes it to, and a texture never set samples transparent black. A texture set here is bound whole and must not be
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

    /// <summary>The shader this material draws with.</summary>
    public Shader Shader { get; }

    /// <summary>A material drawing with <paramref name="shader"/>, with no parameter set.</summary>
    public Material(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        Shader = shader;
        int count = shader.Parameters.Length;
        _values = new Vector4[count];
        _textures = new TextureHandle[count];
    }

    /// <summary>Sets the <c>float</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, float value) => Store(name, ShaderParameterKind.Float, ShaderValue.Of(value));

    /// <summary>Sets the <c>float2</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector2 value) => Store(name, ShaderParameterKind.Vector2, ShaderValue.Of(value));

    /// <summary>Sets the <c>float3</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector3 value) => Store(name, ShaderParameterKind.Vector3, ShaderValue.Of(value));

    /// <summary>Sets the <c>float4</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector4 value) => Store(name, ShaderParameterKind.Vector4, ShaderValue.Of(value));

    /// <summary>
    /// Sets the <c>float4</c> parameter <paramref name="name"/> to <paramref name="value"/>, straight
    /// alpha, each channel from 0 to 1.
    /// </summary>
    public void Set(string name, ColorRgba value) => Store(name, ShaderParameterKind.Vector4, ShaderValue.Of(value));

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
    public Vector2 GetVector2(string name) => ShaderValue.AsVector2(_values[Slot(name, ShaderParameterKind.Vector2)]);

    /// <summary>Reads the <c>float3</c> parameter <paramref name="name"/>, zero until it is set.</summary>
    public Vector3 GetVector3(string name) => ShaderValue.AsVector3(_values[Slot(name, ShaderParameterKind.Vector3)]);

    /// <summary>Reads the <c>float4</c> parameter <paramref name="name"/>, zero until it is set.</summary>
    public Vector4 GetVector4(string name) => _values[Slot(name, ShaderParameterKind.Vector4)];

    /// <summary>
    /// Reads the <c>float4</c> parameter <paramref name="name"/> as a straight-alpha colour, each channel
    /// clamped to 0 to 1 and rounded to the nearest byte. Transparent black until it is set.
    /// </summary>
    public ColorRgba GetColor(string name) => ShaderValue.ColorOf(_values[Slot(name, ShaderParameterKind.Vector4)]);

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

    private void Store(string name, ShaderParameterKind kind, Vector4 value) => _values[Slot(name, kind)] = value;

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
                $"Shader '{Shader.Name}' declares '{name}' in its {ShaderGlobals.Block} block. A material neither sets nor reads it. Use scene.ShaderGlobals.Set or the Get of its kind.",
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
