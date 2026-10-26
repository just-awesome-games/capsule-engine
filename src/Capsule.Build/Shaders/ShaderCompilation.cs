using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>What one compile produced.</summary>
/// <param name="Shaders">A container per format, or null when the compile failed.</param>
/// <param name="Parameters">The parameters a material sets, in declaration order.</param>
/// <param name="Diagnostics">Every error and warning the compiler anchored to a line.</param>
/// <param name="Failure">Why the compile failed where no diagnostic anchors it, or null.</param>
internal readonly record struct ShaderCompilation(
    ShaderContainer[]? Shaders,
    IReadOnlyList<ShaderParameter> Parameters,
    IReadOnlyList<ShaderDiagnostic> Diagnostics,
    string? Failure);
