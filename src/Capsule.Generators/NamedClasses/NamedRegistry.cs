using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>
/// A registry of classes a command line names by their simple class name, which generated code constructs with no
/// arguments: the input drivers <c>--driver</c> takes and the scene payloads <c>--payload</c> takes. One pipeline
/// describes, resolves and writes each.
/// </summary>
/// <param name="Interface">The metadata name of the engine interface a registered class implements.</param>
/// <param name="Registration">The fully qualified registration type each entry constructs.</param>
/// <param name="ClassName">The class a logic assembly's registry is generated as, which also names its file.</param>
/// <param name="Noun">What the generated summary calls one registered class.</param>
/// <param name="Duplicate">The diagnostic a second class claiming a taken name raises.</param>
/// <param name="Plan">The tracking name of the registry's plan step.</param>
internal sealed record NamedRegistry(
    string Interface,
    string Registration,
    string ClassName,
    string Noun,
    DiagnosticDescriptor Duplicate,
    string Plan)
{
    // A driver that takes arguments is left unregistered and reaches a run through WithInputDriver.
    internal static NamedRegistry InputDrivers { get; } = new(
        MetadataNames.InputDriver,
        "global::Capsule.Input.InputDriverRegistration",
        "CapsuleInputDrivers",
        "input driver",
        Diagnostics.DuplicateInputDriverName,
        "InputDriverPlan");

    // A payload that takes arguments is left unregistered, which keeps a base payload out while its presets register.
    internal static NamedRegistry ScenePayloads { get; } = new(
        MetadataNames.ScenePayload,
        "global::Capsule.Scenes.ScenePayloadRegistration",
        "CapsuleScenePayloads",
        "scene payload",
        Diagnostics.DuplicateScenePayloadName,
        "ScenePayloadPlan");
}
