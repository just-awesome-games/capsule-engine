using System.ComponentModel;

namespace Capsule.Rendering;

/// <summary>What kind of value a shader parameter takes. Named by generated code.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum ShaderParameterKind : byte
{
    /// <summary>A <c>float</c>, set from a <see cref="float"/>.</summary>
    Float,

    /// <summary>A <c>float2</c>, set from a <see cref="System.Numerics.Vector2"/>.</summary>
    Vector2,

    /// <summary>A <c>float3</c>, set from a <see cref="System.Numerics.Vector3"/>.</summary>
    Vector3,

    /// <summary>A <c>float4</c>, set from a <see cref="System.Numerics.Vector4"/> or a <see cref="ColorRgba"/>.</summary>
    Vector4,

    /// <summary>A <c>Texture2D</c>, set from a <see cref="Assets.TextureHandle"/>.</summary>
    Texture,
}

/// <summary>One parameter a shader declares, as the build read it from the compiled shader. Named by generated code.</summary>
/// <param name="Name">The parameter's name in the shader source.</param>
/// <param name="Kind">The kind of value it takes.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct ShaderParameter(string Name, ShaderParameterKind Kind);
