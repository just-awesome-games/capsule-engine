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

    private Entry[] _entries = new Entry[4];

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
    public void Set(string name, float value)
    {
        Guard.Finite(value, nameof(value));
        Store(name, ShaderParameterKind.Float, new Vector4(value, 0f, 0f, 0f));
    }

    /// <summary>Sets the <c>float2</c> global <paramref name="name"/>.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    /// <param name="value">The value every material of a shader declaring it reads.</param>
    public void Set(string name, Vector2 value)
    {
        Guard.Finite(value, nameof(value));
        Store(name, ShaderParameterKind.Vector2, new Vector4(value, 0f, 0f));
    }

    /// <summary>Sets the <c>float3</c> global <paramref name="name"/>.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    /// <param name="value">The value every material of a shader declaring it reads.</param>
    public void Set(string name, Vector3 value)
    {
        Vector4 widened = new(value, 0f);
        Guard.Finite(widened, nameof(value));
        Store(name, ShaderParameterKind.Vector3, widened);
    }

    /// <summary>Sets the <c>float4</c> global <paramref name="name"/>.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    /// <param name="value">The value every material of a shader declaring it reads.</param>
    public void Set(string name, Vector4 value)
    {
        Guard.Finite(value, nameof(value));
        Store(name, ShaderParameterKind.Vector4, value);
    }

    /// <summary>
    /// Sets the <c>float4</c> global <paramref name="name"/> to <paramref name="value"/>, straight
    /// alpha, each channel from 0 to 1.
    /// </summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    /// <param name="value">The colour every material of a shader declaring it reads.</param>
    public void Set(string name, ColorRgba value) =>
        Store(name, ShaderParameterKind.Vector4, Material.VectorOf(value));

    /// <summary>Reads the <c>float</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public float GetFloat(string name) => Read(name, ShaderParameterKind.Float).X;

    /// <summary>Reads the <c>float2</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public Vector2 GetVector2(string name)
    {
        Vector4 value = Read(name, ShaderParameterKind.Vector2);

        return new Vector2(value.X, value.Y);
    }

    /// <summary>Reads the <c>float3</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public Vector3 GetVector3(string name)
    {
        Vector4 value = Read(name, ShaderParameterKind.Vector3);

        return new Vector3(value.X, value.Y, value.Z);
    }

    /// <summary>Reads the <c>float4</c> global <paramref name="name"/>, zero until it is set.</summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public Vector4 GetVector4(string name) => Read(name, ShaderParameterKind.Vector4);

    /// <summary>
    /// Reads the <c>float4</c> global <paramref name="name"/> as a straight-alpha colour, each channel
    /// clamped to 0 to 1 and rounded to the nearest byte. Transparent black until it is set.
    /// </summary>
    /// <param name="name">The name in the shader's <c>SceneGlobals</c> block.</param>
    public ColorRgba GetColor(string name) => Material.ColorOf(Read(name, ShaderParameterKind.Vector4));

    // The index of name among the names set, or -1.
    internal int IndexOf(string name)
    {
        for (int i = 0; i < Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    internal ShaderParameterKind KindAt(int index) => _entries[index].Kind;

    internal Vector4 ValueAt(int index) => _entries[index].Value;

    // The value of name, or zero when it was never set. A name set as another kind throws.
    private Vector4 Read(string name, ShaderParameterKind kind)
    {
        int index = Find(name, kind, writing: false);

        return index < 0 ? default : _entries[index].Value;
    }

    private void Store(string name, ShaderParameterKind kind, Vector4 value)
    {
        int index = Find(name, kind, writing: true);
        if (index >= 0)
        {
            _entries[index].Value = value;
            return;
        }

        if (Count == _entries.Length)
        {
            Array.Resize(ref _entries, Count * 2);
        }

        _entries[Count++] = new Entry(name, kind, value);
        Version++;
    }

    // The index of name, or -1 when it was never set. A name set as another kind throws.
    private int Find(string name, ShaderParameterKind kind, bool writing)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        int index = IndexOf(name);
        if (index >= 0 && _entries[index].Kind != kind)
        {
            ShaderParameterKind held = _entries[index].Kind;
            throw new ArgumentException(
                $"Scene global '{name}' was set as a {Material.Spelling(held)} and is {(writing ? "now set" : "read")} as a {Material.Spelling(kind)}. {(writing ? "Pass it " + Material.Argument(held) + " every time" : "Read it with " + Material.Reading(held))}.",
                nameof(name));
        }

        return index;
    }

    private record struct Entry(string Name, ShaderParameterKind Kind, Vector4 Value);
}
