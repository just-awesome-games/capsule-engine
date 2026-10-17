using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>Every scene payload an assembly registers, whether its own registry or the shell's entry point holds them.</summary>
/// <param name="Registered">Every sound payload by qualified name, each accessible to generated code and claiming a name no other class claims.</param>
/// <param name="Diagnostics">Every fault found resolving the plan, reported by whichever file holds the payloads.</param>
internal readonly record struct ScenePayloadPlan(
    EquatableArray<ScenePayloadModel> Registered,
    EquatableArray<Diagnostic> Diagnostics);
