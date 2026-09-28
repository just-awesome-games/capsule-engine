using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>One parameter the pixel stage reads from its constant buffer, at its byte offset.</summary>
internal readonly record struct PixelConstant(string Name, ShaderParameterKind Kind, int Offset);
