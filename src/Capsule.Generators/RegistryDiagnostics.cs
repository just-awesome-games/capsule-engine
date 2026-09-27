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

    internal static readonly DiagnosticDescriptor SpawnNotPassedToBase = Scene(
        "CAP026",
        "An entity must pass its spawn to its base constructor",
        "Entity '{0}' takes an EntitySpawn but does not pass it to its base constructor, so the authored rotation, zIndex and scrollFactor are dropped. Pass the spawn to base");

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

    internal static readonly DiagnosticDescriptor DuplicateAuthorableKey = Scene(
        "CAP033",
        "Two [Authorable] members take one key",
        "'{0}' takes the key '{1}', which the [Authorable] member '{2}' already takes. Rename one of them");

    internal static readonly DiagnosticDescriptor UnclaimedEntryType = Scene(
        "CAP034",
        "A scene document entry's type must name an entity class",
        "Scene document {0}: {1} has type '{2}', which no entity claims. Declare the entity whose namespace names that key, give one [SpawnType(\"{2}\")], or correct the type. Claimed: {3}");

    internal static readonly DiagnosticDescriptor CodeOnlyEntryType = Scene(
        "CAP035",
        "A scene document entry's type must be an entity a placement can construct",
        "Scene document {0}: {1} has type '{2}', but '{3}' has the C# required members {4}, which only code can set. Place this entity in code, or replace required with [Authorable(Required = true)]");

    internal static readonly DiagnosticDescriptor UnknownEntryProperty = Scene(
        "CAP036",
        "A scene document entry sets a key its entity does not declare",
        "Scene document {0}: {1} sets '{2}', which '{3}' does not declare. Its authorable members are: {4}. Remove the key or correct its name");

    internal static readonly DiagnosticDescriptor UnsettableEntryProperty = Scene(
        "CAP037",
        "A scene document entry sets a member a placement cannot set",
        "Scene document {0}: {1} sets '{2}', but '{3}' {4}");

    internal static readonly DiagnosticDescriptor MismatchedEntryProperty = Scene(
        "CAP038",
        "A scene document entry's value has the wrong JSON type",
        "Scene document {0}: {1} sets '{2}' to {3}, but '{4}' takes {5}. Write {6}");

    internal static readonly DiagnosticDescriptor UnknownEntryName = Scene(
        "CAP039",
        "A scene document entry names nothing its member's type declares",
        "Scene document {0}: {1} sets '{2}' to {3}, which names nothing '{4}' declares. Write one of: {5}");

    internal static readonly DiagnosticDescriptor MissingEntryProperty = Scene(
        "CAP040",
        "A scene document entry omits a required member",
        "Scene document {0}: {1} omits '{2}', which '{3}' requires. Add \"{2}\" to the entry's properties, or drop Required = true from the member");

    internal static readonly DiagnosticDescriptor InvalidAuthorableMember = Scene(
        "CAP041",
        "An [Authorable] member must be one a placement can set",
        "'{0}' {1}");

    internal static readonly DiagnosticDescriptor UnknownEntityReference = Scene(
        "CAP042",
        "A scene document entry's reference must name an entity of the document",
        "Scene document {0}: {1} sets '{2}' to {3}, which names no entity in the document. Write the id of an entity entry");

    internal static readonly DiagnosticDescriptor MismatchedEntityReference = Scene(
        "CAP043",
        "A scene document entry's reference must name an entity its member takes",
        "Scene document {0}: {1} sets '{2}' to entity {3}, a '{4}', but '{5}' takes '{6}'. Write the id of an entity that is a '{6}'");

    private const string SegmentGrammar =
        "ASCII letters, digits, hyphens and underscores, starting with a letter";

    private const string KeyGrammar =
        "A key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension";

    private static DiagnosticDescriptor Scene(string id, string title, string message, string page = CapsuleDocs.Scenes) =>
        new(id, title, message, "Capsule.Scenes", DiagnosticSeverity.Error, true, null, CapsuleDocs.At(page));

}
