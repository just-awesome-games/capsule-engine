using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Every diagnostic the generator and the analyzers report, and the suppression the suppressor applies,
// sorted by ID. Each is an error, and each help link opens the docs page that explains its rule.
internal static class Diagnostics
{
    private const string ScenesCategory = "Capsule.Scenes";
    private const string ArchitectureCategory = "Capsule.Architecture";

    private const string ScenesPage = "scenes.md";
    private const string LogicBoundaryPage = "architecture.md#logic-boundary";
    private const string PersistencePage = "persistence.md";

    private const string SegmentGrammar =
        "ASCII letters, digits, hyphens and underscores, starting with a letter";

    private const string KeyGrammar =
        "A key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension";

    internal static readonly DiagnosticDescriptor NotAConcreteEntity = Rule(
        "CAP001", ScenesCategory, ScenesPage,
        "A [SpawnType] class must be a concrete entity",
        "'{0}' is marked [SpawnType] but is not a non-abstract class deriving from Capsule.Scenes.Entity");

    internal static readonly DiagnosticDescriptor MissingSpawnConstructor = Rule(
        "CAP002", ScenesCategory, ScenesPage,
        "A [SpawnType] class must take its spawn data",
        "'{0}' is marked [SpawnType] but has no public constructor taking one Capsule.Scenes.Spawning.EntitySpawn");

    internal static readonly DiagnosticDescriptor DuplicateSpawnType = Rule(
        "CAP003", ScenesCategory, ScenesPage,
        "Two classes claim one spawn type",
        "'{0}' and '{1}' both claim spawn type '{2}'. Give one an explicit [SpawnType(\"type\")]");

    internal static readonly DiagnosticDescriptor BlankSpawnType = Rule(
        "CAP004", ScenesCategory, ScenesPage,
        "A spawn type cannot be blank",
        "'{0}' declares a blank [SpawnType]. Drop the attribute to claim the key its namespace names");

    internal static readonly DiagnosticDescriptor DuplicateSceneDocumentName = Rule(
        "CAP005", ScenesCategory, ScenesPage,
        "Two scenes are composed from one scene document",
        "'{0}' and '{1}' both derive scene document name '{2}'. Rename one so each document composes into one scene");

    internal static readonly DiagnosticDescriptor UnsafeSceneDocumentName = Rule(
        "CAP006", ScenesCategory, ScenesPage,
        "A scene document key must be a portable path",
        "'{0}' claims unsafe scene document key '{1}'. " + KeyGrammar);

    internal static readonly DiagnosticDescriptor SceneDocumentRequiresContentConstructor = Rule(
        "CAP007", ScenesCategory, ScenesPage,
        "[SceneDocument] requires a document-backed scene",
        "'{0}' is marked [SceneDocument] but is not a concrete Capsule.Scenes.Scene with one public constructor taking Capsule.Scenes.SceneContent");

    internal static readonly DiagnosticDescriptor InaccessibleRegisteredType = Rule(
        "CAP008", ScenesCategory, ScenesPage,
        "A registered type must be accessible to generated code",
        "'{0}' has a registry constructor but is private, protected, private protected, or file-local. Make it internal or public");

    internal static readonly DiagnosticDescriptor AmbiguousSceneConstructors = Rule(
        "CAP009", ScenesCategory, ScenesPage,
        "A scene must have one registry constructor shape",
        "'{0}' has both a public parameterless constructor and a public SceneContent constructor, or more than one SceneContent constructor");

    internal static readonly DiagnosticDescriptor AmbiguousEntityConstructors = Rule(
        "CAP010", ScenesCategory, ScenesPage,
        "An entity must have one spawn constructor",
        "'{0}' has more than one public constructor taking Capsule.Scenes.Spawning.EntitySpawn");

    internal static readonly DiagnosticDescriptor ConflictingProjectRoles = Rule(
        "CAP011", ScenesCategory, ScenesPage,
        "A project cannot be both game logic and shell",
        "This project's file declares both <CapsuleGameLogic> and <CapsuleGameShell>. Keep substrate-free game logic and the runtime shell in separate projects, each declaring one of the two");

    internal static readonly DiagnosticDescriptor LogicRoleMissingScenes = Rule(
        "CAP012", ScenesCategory, ScenesPage,
        "A game-logic project must reference Capsule.Scenes",
        "This project's file declares <CapsuleGameLogic> but Capsule.Scenes.Scene is unavailable. Reference Capsule.Scenes or drop the property");

    internal static readonly DiagnosticDescriptor ShellRoleMissingRuntime = Rule(
        "CAP013", ScenesCategory, ScenesPage,
        "A game-shell project must reference Capsule.Runtime",
        "This project's file declares <CapsuleGameShell> but Capsule.Runtime.CapsuleEngine is unavailable. Reference a platform module such as Capsule.Runtime.Desktop or drop the property");

    internal static readonly DiagnosticDescriptor InvalidRegistryProvider = Rule(
        "CAP014", ScenesCategory, ScenesPage,
        "A generated registry provider is invalid",
        "Referenced assembly '{0}' carries invalid Capsule registry metadata. Rebuild it against the same Capsule version as the shell");

    internal static readonly DiagnosticDescriptor ShellRoleMissingLogic = Rule(
        "CAP015", ScenesCategory, ScenesPage,
        "A game-shell project must reference a game-logic assembly",
        "This project's file declares <CapsuleGameShell> but the project references no assembly declaring <CapsuleGameLogic>, so its entry point would name no scenes. Reference the game's logic project");

    internal static readonly DiagnosticDescriptor UnsafeSpawnType = Rule(
        "CAP019", ScenesCategory, ScenesPage,
        "A spawn type must be a portable key",
        "'{0}' claims unsafe spawn type '{1}'. " + KeyGrammar);

    internal static readonly DiagnosticDescriptor DuplicateInputDriverName = Rule(
        "CAP020", ScenesCategory, ScenesPage,
        "Two input drivers claim one name",
        "'{0}' and '{1}' are both named '{2}' on a command line. --driver takes a class name, so rename one");

    internal static readonly DiagnosticDescriptor UnnameableSceneDocumentSegment = Rule(
        "CAP021", ScenesCategory, ScenesPage,
        "A scene document key must be nameable segment by segment",
        "'{0}' claims a scene document key whose segment '{1}' names nothing. Every segment of a key is " + SegmentGrammar);

    internal static readonly DiagnosticDescriptor SpawnNotPassedToBase = Rule(
        "CAP026", ScenesCategory, ScenesPage,
        "An entity must pass its spawn to its base constructor",
        "Entity '{0}' takes an EntitySpawn but does not pass it to its base constructor, so the authored rotation, zIndex and scrollFactor are dropped. Pass the spawn to base");

    internal static readonly DiagnosticDescriptor DocumentBaseSceneConflictsWithAClaim = Rule(
        "CAP027", ScenesCategory, ScenesPage,
        "A document cannot name a baseScene a class also claims",
        "Scene document '{0}' names baseScene '{1}', but '{2}' already claims that document. Drop the class's claim or the document's baseScene, so the document names one base");

    internal static readonly DiagnosticDescriptor InvalidBaseScene = Rule(
        "CAP028", ScenesCategory, ScenesPage,
        "A baseScene must be an abstract Scene a derived type can construct",
        "'{0}' claims baseScene key '{1}', but is not an abstract Capsule.Scenes.Scene with one constructor taking Capsule.Scenes.SceneContent that a derived type can call");

    internal static readonly DiagnosticDescriptor InvalidCamera = Rule(
        "CAP029", ScenesCategory, ScenesPage,
        "A camera must be a concrete Camera with an accessible parameterless constructor",
        "'{0}' claims camera key '{1}', but is not a concrete Capsule.Scenes.Camera with an accessible parameterless constructor");

    internal static readonly DiagnosticDescriptor UnclaimedSceneKey = Rule(
        "CAP030", ScenesCategory, ScenesPage,
        "A baseScene or camera key must name a declared class",
        "Scene document '{0}' names {1} '{2}', which no class claims");

    internal static readonly DiagnosticDescriptor DuplicateCameraKey = Rule(
        "CAP031", ScenesCategory, ScenesPage,
        "Two classes claim one camera key",
        "'{0}' and '{1}' both claim camera key '{2}'. A camera has no attribute to override its key, so rename one class");

    internal static readonly DiagnosticDescriptor DuplicateBaseSceneKey = Rule(
        "CAP032", ScenesCategory, ScenesPage,
        "Two classes claim one baseScene key",
        "'{0}' and '{1}' both claim baseScene key '{2}'. A baseScene has no attribute to override its key, so rename one class");

    internal static readonly DiagnosticDescriptor DuplicateAuthorableKey = Rule(
        "CAP033", ScenesCategory, ScenesPage,
        "Two [Authorable] members take one key",
        "'{0}' takes the key '{1}', which the [Authorable] member '{2}' already takes. Rename one of them");

    internal static readonly DiagnosticDescriptor UnclaimedEntryType = Rule(
        "CAP034", ScenesCategory, ScenesPage,
        "A scene document entry's type must name an entity class",
        "Scene document {0}: {1} has type '{2}', which no entity claims. Declare the entity whose namespace names that key, give one [SpawnType(\"{2}\")], or correct the type. Claimed: {3}");

    internal static readonly DiagnosticDescriptor CodeOnlyEntryType = Rule(
        "CAP035", ScenesCategory, ScenesPage,
        "A scene document entry's type must be an entity a placement can construct",
        "Scene document {0}: {1} has type '{2}', but '{3}' has the C# required members {4}, which only code can set. Place this entity in code, or replace required with [Authorable(Required = true)]");

    internal static readonly DiagnosticDescriptor UnknownEntryProperty = Rule(
        "CAP036", ScenesCategory, ScenesPage,
        "A scene document sets a key its class does not declare",
        "Scene document {0}: {1} sets '{2}', which '{3}' does not declare. Its authorable members are: {4}. Remove the key or correct its name");

    internal static readonly DiagnosticDescriptor UnsettableEntryProperty = Rule(
        "CAP037", ScenesCategory, ScenesPage,
        "A scene document sets a member it cannot set",
        "Scene document {0}: {1} sets '{2}', but '{3}' {4}");

    internal static readonly DiagnosticDescriptor MismatchedEntryProperty = Rule(
        "CAP038", ScenesCategory, ScenesPage,
        "A scene document value has the wrong JSON type",
        "Scene document {0}: {1} sets {2} to {3}, but '{4}' takes {5}. Write {6}");

    internal static readonly DiagnosticDescriptor UnknownEntryName = Rule(
        "CAP039", ScenesCategory, ScenesPage,
        "A scene document value names nothing its member's type declares",
        "Scene document {0}: {1} sets {2} to {3}, which names nothing '{4}' declares. Write one of: {5}");

    internal static readonly DiagnosticDescriptor MissingEntryProperty = Rule(
        "CAP040", ScenesCategory, ScenesPage,
        "A scene document omits a required member",
        "Scene document {0}: {1} omits '{2}', which '{3}' requires. Add \"{2}\" to its properties, or drop Required = true from the member");

    internal static readonly DiagnosticDescriptor InvalidAuthorableMember = Rule(
        "CAP041", ScenesCategory, ScenesPage,
        "An [Authorable] member must be one a placement can set",
        "'{0}' {1}");

    internal static readonly DiagnosticDescriptor UnknownEntityReference = Rule(
        "CAP042", ScenesCategory, ScenesPage,
        "A scene document reference must name an entity of the document",
        "Scene document {0}: {1} sets {2} to {3}, which names no entity in the document. Write the id of an entity entry");

    internal static readonly DiagnosticDescriptor MismatchedEntityReference = Rule(
        "CAP043", ScenesCategory, ScenesPage,
        "A scene document reference must name an entity its member takes",
        "Scene document {0}: {1} sets {2} to entity {3}, a '{4}', but '{5}' takes '{6}'. Write the id of an entity that is a '{6}'");

    internal static readonly DiagnosticDescriptor UnknownAssetKey = Rule(
        "CAP044", ScenesCategory, ScenesPage,
        "A scene document asset key must name an asset the game ships",
        "Scene document {0}: {1} sets {2} to {3}, but no {4} keys as \"{5}\". {6}");

    internal static readonly DiagnosticDescriptor RuntimeBoundary = Rule(
        "CAP100", ArchitectureCategory, LogicBoundaryPage,
        "Game logic cannot reference the runtime",
        "Game-logic assembly '{0}' references '{1}'. Runtime access belongs in the shell");

    internal static readonly DiagnosticDescriptor PlatformBoundary = Rule(
        "CAP101", ArchitectureCategory, LogicBoundaryPage,
        "Game projects cannot reference MonoGame directly",
        "Capsule project '{0}' references '{1}' directly. Platform APIs belong behind Capsule.Runtime");

    internal static readonly DiagnosticDescriptor ExternalIo = Rule(
        "CAP102", ArchitectureCategory, LogicBoundaryPage,
        "Game logic cannot perform external I/O",
        "'{0}' performs external I/O. Read shipped files through CapsuleAssets, and leave every other file, socket and device to the shell");

    internal static readonly DiagnosticDescriptor Concurrency = Rule(
        "CAP103", ArchitectureCategory, LogicBoundaryPage,
        "Game logic cannot schedule ambient concurrency",
        "'{0}' schedules work outside the deterministic simulation. Do the work inside the step");

    internal static readonly DiagnosticDescriptor AmbientTime = Rule(
        "CAP104", ArchitectureCategory, LogicBoundaryPage,
        "Game logic cannot read ambient time",
        "'{0}' reads process or wall-clock time. Use the simulation time the step is given, reached from a scene, entity or component as StepContext.TotalSeconds or StepContext.DeltaSeconds");

    internal static readonly DiagnosticDescriptor AmbientRandom = Rule(
        "CAP105", ArchitectureCategory, LogicBoundaryPage,
        "Game logic cannot use randomness outside the run's seeded source",
        "'{0}' is not reproducible across runs or runtime versions. Draw from the run's seeded source, reached from a scene, entity or component as Random");

    internal static readonly DiagnosticDescriptor InitOnlySaveDocumentProperty = Rule(
        "CAP106", ArchitectureCategory, PersistencePage,
        "Save document properties must be settable",
        "Save document property '{0}' is init-only. Declare it with set. An absent field in an older save would otherwise read as default, not as its initializer");

    internal static readonly DiagnosticDescriptor PlatformMath = Rule(
        "CAP107", ArchitectureCategory, LogicBoundaryPage,
        "Game logic cannot call a platform transcendental that DeterministicMath replaces",
        "'{0}' differs between operating systems. Call {1}");

    internal static readonly SuppressionDescriptor UnassignedAuthorableField = new(
        "CAP108", "CS0649", "A scene document placement writes this [Authorable] field.");

    private static DiagnosticDescriptor Rule(string id, string category, string page, string title, string message) =>
        new(
            id, title, message, category, DiagnosticSeverity.Error, true, null,
            "https://github.com/just-awesome-games/capsule-engine/blob/main/docs/" + page);
}
