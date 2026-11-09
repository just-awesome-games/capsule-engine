using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>Every class one <see cref="NamedRegistry"/> registers, whether the assembly's own registry or the shell's entry point holds them.</summary>
/// <param name="Registered">Every sound class by qualified name, each accessible to generated code and claiming a name no other class claims.</param>
/// <param name="Diagnostics">Every fault found resolving the plan, reported by whichever file holds the classes.</param>
internal readonly record struct NamedClassPlan(
    EquatableArray<NamedClassModel> Registered,
    EquatableArray<Diagnostic> Diagnostics);
