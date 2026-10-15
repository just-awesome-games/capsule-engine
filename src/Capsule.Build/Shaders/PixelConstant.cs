using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>One parameter the pixel stage reads from a constant buffer, at its byte offset in that buffer.</summary>
/// <param name="Name">The parameter's name in the shader source.</param>
/// <param name="Kind">The kind of value it takes.</param>
/// <param name="Offset">Its byte offset in its buffer.</param>
/// <param name="Global">Whether it lives in the scene globals buffer and not the material's.</param>
internal readonly record struct PixelConstant(string Name, ShaderParameterKind Kind, int Offset, bool Global = false);
