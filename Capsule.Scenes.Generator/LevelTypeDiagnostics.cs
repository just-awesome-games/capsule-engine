using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

/// <summary>
/// Everything <c>[LevelType]</c> can be wrong about. All errors: each one would otherwise
/// surface as a level that fails to spawn at run time, on a machine that is not the author's.
/// </summary>
internal static class LevelTypeDiagnostics
{
    private const string Category = "Capsule.Scenes";

    internal static readonly DiagnosticDescriptor NotAConcreteEntity = new(
        "CAP001",
        "A [LevelType] class must be a concrete entity",
        "'{0}' is marked [LevelType] but is not a non-abstract class deriving from Capsule.Scenes.Entity",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MissingSpawnConstructor = new(
        "CAP002",
        "A [LevelType] class must take its spawn data",
        "'{0}' is marked [LevelType] but has no public constructor taking one Capsule.Scenes.Spawning.EntitySpawn",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateLevelTypeId = new(
        "CAP003",
        "Two classes claim one level type",
        "'{0}' and '{1}' both claim level type '{2}'; give one an explicit [LevelType(\"type\")]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor BlankLevelTypeId = new(
        "CAP004",
        "A level type cannot be blank",
        "'{0}' declares a blank [LevelType]; drop the argument to claim its kebab-cased class name",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
