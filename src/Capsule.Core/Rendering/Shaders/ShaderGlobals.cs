using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// The values of the shader parameters a scene sets for every material, reached as <c>scene.ShaderGlobals</c>.
/// </summary>
/// <remarks>
/// A shader declares such a parameter in its <c>cbuffer SceneGlobals</c> block, and every material
/// drawing that shader reads the scene's value. A new scene starts with every global at zero, and a
/// global never set reads zero. A global is a <c>float</c>, <c>float2</c>, <c>float3</c> or
/// <c>float4</c>, and a texture stays a material parameter. A name keeps the kind it was first set
/// with, and drawing throws when a shader declares it as another. A name no shader declares is ignored.
/// A global splits no batch.
/// </remarks>
/// <example>
/// <code>
/// // The shader: cbuffer SceneGlobals { float3 WorldAdd; };
/// Scene.ShaderGlobals.Set("WorldAdd", new Vector3(0.4f, 0.4f, 0.4f));
/// </code>
/// </example>
public sealed class ShaderGlobals
{
    // The name of the block a shader declares its scene globals in.
    internal const string Block = "SceneGlobals";

    // The names set so far, each with the kind it was first set as, in the order they were set.
    private ShaderParameter[] _declared = new ShaderParameter[4];
    private Vector4[] _values = new Vector4[4];

    // How many names have been set. A name is never removed.
    internal int Count { get; private set; }

    // Changes when a name is added, which is when a shader's resolved indices go stale.
    internal int Version { get; private set; }

    internal ShaderGlobals()
    {
    }

    /// <summary>Sets the <c>float</c> global <paramref name="name"/>.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    /// <param name="value">The value every material of a shader declaring it reads.</param>
    public void Set(string name, float value) => Store(name, ShaderParameterKind.Float, ShaderValue.Of(value));

    /// <summary>Sets the <c>float2</c> global <paramref name="name"/>.</summary>
    /// <inheritdoc cref="Set(string, float)" path="/param"/>
    public void Set(string name, Vector2 value) => Store(name, ShaderParameterKind.Vector2, ShaderValue.Of(value));

    /// <summary>Sets the <c>float3</c> global <paramref name="name"/>.</summary>
    /// <inheritdoc cref="Set(string, float)" path="/param"/>
    public void Set(string name, Vector3 value) => Store(name, ShaderParameterKind.Vector3, ShaderValue.Of(value));

    /// <summary>Sets the <c>float4</c> global <paramref name="name"/>.</summary>
    /// <inheritdoc cref="Set(string, float)" path="/param"/>
    public void Set(string name, Vector4 value) => Store(name, ShaderParameterKind.Vector4, ShaderValue.Of(value));

    /// <summary>
    /// Sets the <c>float4</c> global <paramref name="name"/> to <paramref name="value"/>, straight
    /// alpha, each channel from 0 to 1.
    /// </summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    /// <param name="value">The colour every material of a shader declaring it reads.</param>
    public void Set(string name, ColorRgba value) => Store(name, ShaderParameterKind.Vector4, ShaderValue.Of(value));

    /// <summary>Reads the <c>float</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public float GetFloat(string name) => Read(name, ShaderParameterKind.Float).X;

    /// <summary>Reads the <c>float2</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public Vector2 GetVector2(string name) => ShaderValue.AsVector2(Read(name, ShaderParameterKind.Vector2));

    /// <summary>Reads the <c>float3</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public Vector3 GetVector3(string name) => ShaderValue.AsVector3(Read(name, ShaderParameterKind.Vector3));

    /// <summary>Reads the <c>float4</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public Vector4 GetVector4(string name) => Read(name, ShaderParameterKind.Vector4);

    /// <summary>
    /// Reads the <c>float4</c> global <paramref name="name"/> as a straight-alpha colour, each channel
    /// clamped to 0 to 1 and rounded to the nearest byte. Transparent black until it is set.
    /// </summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public ColorRgba GetColor(string name) => ShaderValue.ColorOf(Read(name, ShaderParameterKind.Vector4));

    // The index of name among the names set, or -1.
    internal int IndexOf(string name) => ShaderValue.IndexOf(_declared.AsSpan(0, Count), name);

    internal ShaderParameterKind KindAt(int index) => _declared[index].Kind;

    internal Vector4 ValueAt(int index) => _values[index];

    // The value of name, or zero when it was never set. A name set as another kind throws.
    private Vector4 Read(string name, ShaderParameterKind kind)
    {
        int index = Find(name, kind, writing: false);

        return index < 0 ? default : _values[index];
    }

    private void Store(string name, ShaderParameterKind kind, Vector4 value)
    {
        int index = Find(name, kind, writing: true);
        if (index >= 0)
        {
            _values[index] = value;
            return;
        }

        if (Count == _declared.Length)
        {
            Array.Resize(ref _declared, Count * 2);
            Array.Resize(ref _values, Count * 2);
        }

        _declared[Count] = new ShaderParameter(name, kind, Global: true);
        _values[Count++] = value;
        Version++;
    }

    // The index of name, or -1 when it was never set. A name set as another kind throws.
    private int Find(string name, ShaderParameterKind kind, bool writing)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        int index = IndexOf(name);
        if (index >= 0 && _declared[index].Kind != kind)
        {
            ShaderParameterKind held = _declared[index].Kind;
            throw new ArgumentException(
                $"Scene global '{name}' was set as a {Material.Spelling(held)} and is {(writing ? "now set" : "read")} as a {Material.Spelling(kind)}. {(writing ? "Pass it " + Material.Argument(held) + " every time" : "Read it with " + Material.Reading(held))}.",
                nameof(name));
        }

        return index;
    }
}
