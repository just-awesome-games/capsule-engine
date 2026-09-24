using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>A shader and the values of its parameters, drawn by every renderer that holds it.</summary>
/// <remarks>
/// Neighbouring renderers draw in one batch when they hold the same instance and the same texture,
/// so share one material across the renderers that look alike. A parameter never set reads zero,
/// whatever the shader source initializes it to. A texture set here is bound whole and must not be
/// packed into an atlas. <c>Set</c> throws when the shader declares no parameter of that name, or
/// declares it as another kind, and the message lists what it declares.
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
    private readonly bool[] _set;

    /// <summary>A material drawing with <paramref name="shader"/>, with no parameter set.</summary>
    public Material(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);

        Shader = shader;
        int count = shader.Parameters.Length;
        _values = new Vector4[count];
        _textures = new TextureHandle[count];
        _set = new bool[count];
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
        Finite(widened);
        _values[Slot(name, ShaderParameterKind.Vector3)] = widened;
    }

    /// <summary>Sets the <c>float4</c> parameter <paramref name="name"/>.</summary>
    public void Set(string name, Vector4 value)
    {
        Finite(value);
        _values[Slot(name, ShaderParameterKind.Vector4)] = value;
    }

    /// <summary>
    /// Sets the <c>float4</c> parameter <paramref name="name"/> to <paramref name="value"/>, straight
    /// alpha, each channel from 0 to 1.
    /// </summary>
    public void Set(string name, ColorRgba value) =>
        _values[Slot(name, ShaderParameterKind.Vector4)] = new Vector4(value.R, value.G, value.B, value.A) / 255f;

    /// <summary>
    /// Sets the <c>Texture2D</c> parameter <paramref name="name"/>, which the shader reads with the
    /// frame's sampling, clamped at its edges.
    /// </summary>
    /// <remarks>
    /// The texture is bound whole, and drawing throws when the build packed it into an atlas. Keep it
    /// out of every atlas manifest.
    /// </remarks>
    public void Set(string name, TextureHandle value)
    {
        if (value.Name is null || value.Extension is null)
        {
            throw new ArgumentException("A default TextureHandle names no texture. Pass a texture's CapsuleAssets member.", nameof(value));
        }

        _textures[Slot(name, ShaderParameterKind.Texture)] = value;
    }

    // The value of parameter index, zero until set. A vector of fewer than four components fills the
    // rest with zero.
    internal Vector4 Value(int index) => _values[index];

    // The texture set on parameter index, or false when none was.
    internal bool TryGetTexture(int index, out TextureHandle texture)
    {
        texture = _textures[index];

        return _set[index];
    }

    private static void Finite(Vector4 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Expected finite components.");
        }
    }

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

        ShaderParameterKind declared = Shader.Parameters[index].Kind;
        if (declared != kind)
        {
            throw new ArgumentException(
                $"Shader '{Shader.Name}' declares '{name}' as a {Spelling(declared)}. Pass it {Argument(declared)}.",
                nameof(name));
        }

        _set[index] = true;

        return index;
    }

    private string Declared()
    {
        ReadOnlySpan<ShaderParameter> parameters = Shader.Parameters;
        if (parameters.IsEmpty)
        {
            return "no parameters";
        }

        string[] names = new string[parameters.Length];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = $"'{parameters[i].Name}' ({Spelling(parameters[i].Kind)})";
        }

        return string.Join(", ", names);
    }

    private static string Spelling(ShaderParameterKind kind) => kind switch
    {
        ShaderParameterKind.Float => "float",
        ShaderParameterKind.Vector2 => "float2",
        ShaderParameterKind.Vector3 => "float3",
        ShaderParameterKind.Vector4 => "float4",
        _ => "texture",
    };

    private static string Argument(ShaderParameterKind kind) => kind switch
    {
        ShaderParameterKind.Float => "a float",
        ShaderParameterKind.Vector2 => "a Vector2",
        ShaderParameterKind.Vector3 => "a Vector3",
        ShaderParameterKind.Vector4 => "a Vector4 or a ColorRgba",
        _ => "a TextureHandle",
    };
}
