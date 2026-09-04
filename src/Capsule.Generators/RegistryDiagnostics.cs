using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

internal static class RegistryDiagnostics
{
    internal static readonly DiagnosticDescriptor NotAConcreteEntity = Scene(
        "CAP001",
        "A [SpawnType] class must be a concrete entity",
        "'{0}' is marked [SpawnType] but is not a non-abstract class deriving from Capsule.Scenes.Entity");

    internal static readonly DiagnosticDescriptor MissingSpawnConstructor = Scene(
        "CAP002",
        "A [SpawnType] class must take its spawn data",
        "'{0}' is marked [SpawnType] but has no public constructor taking one Capsule.Scenes.Spawning.EntitySpawn");

    internal static readonly DiagnosticDescriptor DuplicateSpawnType = Scene(
        "CAP003",
        "Two classes claim one spawn type",
        "'{0}' and '{1}' both claim spawn type '{2}'; give one an explicit [SpawnType(\"type\")]");

    internal static readonly DiagnosticDescriptor BlankSpawnType = Scene(
        "CAP004",
        "A spawn type cannot be blank",
        "'{0}' declares a blank [SpawnType]; drop the attribute to claim the key its namespace names");

    internal static readonly DiagnosticDescriptor DuplicateSceneDocumentName = Scene(
        "CAP005",
        "Two scenes are composed from one scene document",
        "'{0}' and '{1}' both derive scene document name '{2}'; rename one so each document composes into one scene");

    internal static readonly DiagnosticDescriptor UnsafeSceneDocumentName = Scene(
        "CAP006",
        "A scene document key must be a portable path",
        "'{0}' claims unsafe scene document key '{1}'; " + KeyGrammar);

    internal static readonly DiagnosticDescriptor SceneDocumentRequiresContentConstructor = Scene(
        "CAP007",
        "[SceneDocument] requires a document-backed scene",
        "'{0}' is marked [SceneDocument] but is not a concrete Capsule.Scenes.Scene with one public constructor taking Capsule.Scenes.SceneContent");

    internal static readonly DiagnosticDescriptor InaccessibleRegisteredType = Scene(
        "CAP008",
        "A registered type must be accessible to generated code",
        "'{0}' has a registry constructor but is private, protected, private protected, or file-local; make it internal or public");

    internal static readonly DiagnosticDescriptor AmbiguousSceneConstructors = Scene(
        "CAP009",
        "A scene must have one registry constructor shape",
        "'{0}' has both a public parameterless constructor and a public SceneContent constructor, or more than one SceneContent constructor");

    internal static readonly DiagnosticDescriptor AmbiguousEntityConstructors = Scene(
        "CAP010",
        "An entity must have one spawn constructor",
        "'{0}' has more than one public constructor taking Capsule.Scenes.Spawning.EntitySpawn");

    internal static readonly DiagnosticDescriptor ConflictingProjectRoles = Scene(
        "CAP011",
        "A project cannot be both game logic and shell",
        "This project declares both CapsuleGameLogic and CapsuleGameShell; keep substrate-free game logic and the runtime shell in separate projects");

    internal static readonly DiagnosticDescriptor LogicRoleMissingScenes = Scene(
        "CAP012",
        "A game-logic project must reference Capsule.Scenes",
        "This project declares CapsuleGameLogic but Capsule.Scenes.Scene is unavailable; reference Capsule.Scenes or remove the role");

    internal static readonly DiagnosticDescriptor ShellRoleMissingRuntime = Scene(
        "CAP013",
        "A game-shell project must reference Capsule.Runtime",
        "This project declares CapsuleGameShell but Capsule.Runtime.CapsuleEngine is unavailable; reference Capsule.Runtime or remove the role");

    internal static readonly DiagnosticDescriptor InvalidRegistryProvider = Scene(
        "CAP014",
        "A generated registry provider is invalid",
        "Referenced assembly '{0}' carries invalid Capsule registry metadata; rebuild it against the same Capsule version as the shell");

    internal static readonly DiagnosticDescriptor ShellRoleMissingLogic = Scene(
        "CAP015",
        "A game-shell project must reference a game-logic assembly",
        "This project declares CapsuleGameShell but references no assembly declaring CapsuleGameLogic, so its entry point would name no scenes; reference the game's logic project");

    internal static readonly DiagnosticDescriptor DuplicateAssetIdentifier = Asset(
        "CAP016",
        "Two sources in one directory claim one name",
        "'{0}' and '{1}' both declare '{2}' in '{3}'; two names that differ only in their separators are one C# name, so rename one");

    internal static readonly DiagnosticDescriptor UnsafeAssetName = Asset(
        "CAP017",
        "An asset name must become an identifier",
        "'{0}' cannot be named in code; every directory and file name under a domain root is ASCII letters, digits, hyphens and underscores, starting with a letter");

    internal static readonly DiagnosticDescriptor AssetNamedAfterItsDomain = Asset(
        "CAP018",
        "A source cannot take a name its enclosing class reserves",
        "'{0}' declares '{1}' in '{2}', which is the name of the class that directory is declared as or of the 'All' member the generated registry declares on it; rename the file or its directory");

    internal static readonly DiagnosticDescriptor UnsafeSpawnType = Scene(
        "CAP019",
        "A spawn type must be a portable key",
        "'{0}' claims unsafe spawn type '{1}'; " + KeyGrammar);

    private const string KeyGrammar =
        "a key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension";

    private static DiagnosticDescriptor Scene(string id, string title, string message) =>
        new(id, title, message, "Capsule.Scenes", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static DiagnosticDescriptor Asset(string id, string title, string message) =>
        new(id, title, message, "Capsule.Assets", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
