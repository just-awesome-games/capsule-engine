using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>What a compiled shader hands on: its parameter table and the warnings its compile reported.</summary>
/// <param name="Parameters">The parameters a material sets, in declaration order.</param>
/// <param name="ReadsScreen">Whether the fragment calls <c>SampleScreen</c>.</param>
/// <param name="Warnings">Every warning the compiler anchored to a line, reported again whenever the shader is reused.</param>
internal sealed record ShaderFacts(ShaderParameter[] Parameters, bool ReadsScreen, ShaderDiagnostic[] Warnings);
