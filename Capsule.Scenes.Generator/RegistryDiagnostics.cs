using Microsoft.CodeAnalysis;

namespace Capsule.Scenes.Generator;

internal static class RegistryDiagnostics
{
    private const string Category = "Capsule.Scenes";
    private const string AssetCategory = "Capsule.Assets";

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

    internal static readonly DiagnosticDescriptor UnsafeMapName = new(
        "CAP006",
        "A map name must be a portable file stem",
        "'{0}' claims unsafe map name '{1}'; use only ASCII letters, digits, hyphens, and underscores",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MapNameRequiresMapScene = new(
        "CAP007",
        "[MapName] requires a map-backed scene",
        "'{0}' is marked [MapName] but is not a concrete Capsule.Scenes.Scene with one public constructor taking Capsule.Scenes.MapSceneContext",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InaccessibleRegisteredType = new(
        "CAP008",
        "A registered type must be accessible to generated code",
        "'{0}' has a registry constructor but is private, protected, private protected, or file-local; make it internal or public",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousSceneConstructors = new(
        "CAP009",
        "A scene must have one registry constructor shape",
        "'{0}' has both a public parameterless constructor and a public MapSceneContext constructor, or more than one MapSceneContext constructor",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AmbiguousEntityConstructors = new(
        "CAP010",
        "An entity must have one spawn constructor",
        "'{0}' has more than one public constructor taking Capsule.Scenes.Spawning.EntitySpawn",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ConflictingProjectRoles = new(
        "CAP011",
        "A project cannot be both game logic and shell",
        "This project declares both CapsuleGameLogic and CapsuleGameShell; keep substrate-free game logic and the runtime shell in separate projects",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LogicRoleMissingScenes = new(
        "CAP012",
        "A game-logic project must reference Capsule.Scenes",
        "This project declares CapsuleGameLogic but Capsule.Scenes.Scene is unavailable; reference Capsule.Scenes or remove the role",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ShellRoleMissingRuntime = new(
        "CAP013",
        "A game-shell project must reference Capsule.Runtime",
        "This project declares CapsuleGameShell but Capsule.Runtime.CapsuleEngine is unavailable; reference Capsule.Runtime or remove the role",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidRegistryProvider = new(
        "CAP014",
        "A generated registry provider is invalid",
        "Referenced assembly '{0}' carries invalid Capsule registry metadata; rebuild it against the same Capsule version as the shell",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ShellRoleMissingLogic = new(
        "CAP015",
        "A game-shell project must reference a game-logic assembly",
        "This project declares CapsuleGameShell but references no assembly declaring CapsuleGameLogic, so its entry point would name no scenes; reference the game's logic project",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateAssetIdentifier = new(
        "CAP016",
        "Two assets claim one name",
        "'{0}' and '{1}' both name asset '{2}' in the '{3}' domain; rename one so each asset has a name of its own",
        AssetCategory,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsafeAssetName = new(
        "CAP017",
        "An asset name must become an identifier",
        "'{0}' cannot be named in code; use ASCII letters, digits, hyphens and underscores, starting with a letter",
        AssetCategory,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor AssetNamedAfterItsDomain = new(
        "CAP018",
        "An asset cannot be named after its domain",
        "'{0}' names asset '{1}', which is the class its domain is declared as; rename the file so the member and the class it sits on differ",
        AssetCategory,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
