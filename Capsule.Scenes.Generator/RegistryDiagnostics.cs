using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

/// <summary>
/// Everything <c>[SpawnType]</c> can be wrong about, plus the collisions two classes can reach
/// without it. All errors: each one would otherwise surface as authored content that fails to load
/// at run time, on a machine that is not the author's.
/// </summary>
internal static class RegistryDiagnostics
{
    private const string Category = "Capsule.Scenes";

    internal static readonly DiagnosticDescriptor NotAConcreteEntity = new(
        "CAP001",
        "A [SpawnType] class must be a concrete entity",
        "'{0}' is marked [SpawnType] but is not a non-abstract class deriving from Capsule.Scenes.Entity",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MissingSpawnConstructor = new(
        "CAP002",
        "A [SpawnType] class must take its spawn data",
        "'{0}' is marked [SpawnType] but has no public constructor taking one Capsule.Scenes.Spawning.EntitySpawn",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateSpawnType = new(
        "CAP003",
        "Two classes claim one spawn type",
        "'{0}' and '{1}' both claim spawn type '{2}'; give one an explicit [SpawnType(\"type\")]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor BlankSpawnType = new(
        "CAP004",
        "A spawn type cannot be blank",
        "'{0}' declares a blank [SpawnType]; drop the attribute to claim its kebab-cased class name",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateSceneMapName = new(
        "CAP005",
        "Two scenes are composed from one map",
        "'{0}' and '{1}' both derive map name '{2}'; rename one so each map composes into one scene",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
