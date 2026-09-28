using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>Every input driver an assembly registers, whether its own registry or the shell's entry point holds them.</summary>
/// <param name="Registered">Every sound driver in declaration order, each accessible to generated code and claiming a name no other class claims.</param>
/// <param name="Diagnostics">Every fault found resolving the plan, reported by whichever file holds the drivers.</param>
internal readonly record struct InputDriverPlan(
    EquatableArray<InputDriverModel> Registered,
    EquatableArray<Diagnostic> Diagnostics);
