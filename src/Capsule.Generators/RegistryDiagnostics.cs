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
        "'{0}' and '{1}' both claim spawn type '{2}'. Give one an explicit [SpawnType(\"type\")]");

    internal static readonly DiagnosticDescriptor BlankSpawnType = Scene(
        "CAP004",
        "A spawn type cannot be blank",
        "'{0}' declares a blank [SpawnType]. Drop the attribute to claim the key its namespace names");

    internal static readonly DiagnosticDescriptor DuplicateSceneDocumentName = Scene(
        "CAP005",
        "Two scenes are composed from one scene document",
        "'{0}' and '{1}' both derive scene document name '{2}'. Rename one so each document composes into one scene");

    internal static readonly DiagnosticDescriptor UnsafeSceneDocumentName = Scene(
        "CAP006",
        "A scene document key must be a portable path",
        "'{0}' claims unsafe scene document key '{1}'. " + KeyGrammar);

    internal static readonly DiagnosticDescriptor SceneDocumentRequiresContentConstructor = Scene(
        "CAP007",
        "[SceneDocument] requires a document-backed scene",
        "'{0}' is marked [SceneDocument] but is not a concrete Capsule.Scenes.Scene with one public constructor taking Capsule.Scenes.SceneContent");

    internal static readonly DiagnosticDescriptor InaccessibleRegisteredType = Scene(
        "CAP008",
        "A registered type must be accessible to generated code",
        "'{0}' has a registry constructor but is private, protected, private protected, or file-local. Make it internal or public");

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
        "This project's file declares both <CapsuleGameLogic> and <CapsuleGameShell>. Keep substrate-free game logic and the runtime shell in separate projects, each declaring one of the two");

    internal static readonly DiagnosticDescriptor LogicRoleMissingScenes = Scene(
        "CAP012",
        "A game-logic project must reference Capsule.Scenes",
        "This project's file declares <CapsuleGameLogic> but Capsule.Scenes.Scene is unavailable. Reference Capsule.Scenes or drop the property");

    internal static readonly DiagnosticDescriptor ShellRoleMissingRuntime = Scene(
        "CAP013",
        "A game-shell project must reference Capsule.Runtime",
        "This project's file declares <CapsuleGameShell> but Capsule.Runtime.CapsuleEngine is unavailable. Reference a platform module such as Capsule.Runtime.Desktop or drop the property");

    internal static readonly DiagnosticDescriptor InvalidRegistryProvider = Scene(
        "CAP014",
        "A generated registry provider is invalid",
        "Referenced assembly '{0}' carries invalid Capsule registry metadata. Rebuild it against the same Capsule version as the shell");

    internal static readonly DiagnosticDescriptor ShellRoleMissingLogic = Scene(
        "CAP015",
        "A game-shell project must reference a game-logic assembly",
        "This project's file declares <CapsuleGameShell> but the project references no assembly declaring <CapsuleGameLogic>, so its entry point would name no scenes. Reference the game's logic project");

    internal static readonly DiagnosticDescriptor DuplicateAssetIdentifier = Asset(
        "CAP016",
        "Two sources in one directory claim one name",
        "'{0}' and '{1}' both declare '{2}' in '{3}'. Two names differing only in their separators are one C# name, so rename one");

    internal static readonly DiagnosticDescriptor UnsafeAssetName = Asset(
        "CAP017",
        "An asset name must become an identifier",
        "'{0}' cannot be named in code. Every directory and file name under a domain root is " + SegmentGrammar);

    internal static readonly DiagnosticDescriptor AssetNamedAfterItsDomain = Asset(
        "CAP018",
        "A source cannot take a name its enclosing class reserves",
        "'{0}' declares '{1}' in '{2}', a name the generated registry reserves there. Rename the file or its directory");

    internal static readonly DiagnosticDescriptor UnsafeSpawnType = Scene(
        "CAP019",
        "A spawn type must be a portable key",
        "'{0}' claims unsafe spawn type '{1}'. " + KeyGrammar);

    internal static readonly DiagnosticDescriptor DuplicateInputDriverName = Scene(
        "CAP020",
        "Two input drivers claim one name",
        "'{0}' and '{1}' are both named '{2}' on a command line. --driver takes a class name, so rename one");

    internal static readonly DiagnosticDescriptor UnnameableSceneDocumentSegment = Scene(
        "CAP021",
        "A scene document key must be nameable segment by segment",
        "'{0}' claims a scene document key whose segment '{1}' names nothing. Every segment of a key is " + SegmentGrammar);

    internal static readonly DiagnosticDescriptor UnreadableFont = Asset(
        "CAP022",
        "A bitmap font source cannot be compiled",
        "'{0}' {1}",
        CapsuleDocs.Fonts);

    internal static readonly DiagnosticDescriptor UnshippedFontPage = Asset(
        "CAP023",
        "A bitmap font names a page the game does not ship",
        "'{0}' {1}",
        CapsuleDocs.Fonts);

    internal static readonly DiagnosticDescriptor SpawnNotPassedToBase = Scene(
        "CAP026",
        "An entity must pass its spawn to its base constructor",
        "Entity '{0}' takes an EntitySpawn but does not pass it to its base constructor, so the authored zIndex and scrollFactor are dropped. Pass the spawn to base");

    internal static readonly DiagnosticDescriptor DocumentBaseSceneConflictsWithAClaim = Scene(
        "CAP027",
        "A document cannot name a baseScene a class also claims",
        "Scene document '{0}' names baseScene '{1}', but '{2}' already claims that document. Drop the class's claim or the document's baseScene, so the document names one base");

    internal static readonly DiagnosticDescriptor InvalidBaseScene = Scene(
        "CAP028",
        "A baseScene must be an abstract Scene a derived type can construct",
        "'{0}' claims baseScene key '{1}', but is not an abstract Capsule.Scenes.Scene with one constructor taking Capsule.Scenes.SceneContent that a derived type can call");

    internal static readonly DiagnosticDescriptor InvalidCamera = Scene(
        "CAP029",
        "A camera must be a concrete Camera with an accessible parameterless constructor",
        "'{0}' claims camera key '{1}', but is not a concrete Capsule.Scenes.Camera with an accessible parameterless constructor");

    internal static readonly DiagnosticDescriptor UnclaimedSceneKey = Scene(
        "CAP030",
        "A baseScene or camera key must name a declared class",
        "Scene document '{0}' names {1} '{2}', which no class claims");

    internal static readonly DiagnosticDescriptor DuplicateCameraKey = Scene(
        "CAP031",
        "Two classes claim one camera key",
        "'{0}' and '{1}' both claim camera key '{2}'. A camera has no attribute to override its key, so rename one class");

    internal static readonly DiagnosticDescriptor DuplicateBaseSceneKey = Scene(
        "CAP032",
        "Two classes claim one baseScene key",
        "'{0}' and '{1}' both claim baseScene key '{2}'. A baseScene has no attribute to override its key, so rename one class");

    private const string SegmentGrammar =
        "ASCII letters, digits, hyphens and underscores, starting with a letter";

    private const string KeyGrammar =
        "A key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension";

    private static DiagnosticDescriptor Scene(string id, string title, string message, string page = CapsuleDocs.Scenes) =>
        new(id, title, message, "Capsule.Scenes", DiagnosticSeverity.Error, true, null, CapsuleDocs.At(page));

    private static DiagnosticDescriptor Asset(string id, string title, string message, string page = CapsuleDocs.NamedAssets) =>
        new(id, title, message, "Capsule.Assets", DiagnosticSeverity.Error, true, null, CapsuleDocs.At(page));
}
