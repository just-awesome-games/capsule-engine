using System.Runtime.CompilerServices;
using Capsule.AotSmoke.Logic;
using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Generated;

namespace Capsule.AotSmoke;

internal static class Program
{
    private const int IdleSteps = 60;

    private const long DrivenSteps = IdleSteps + 1;

    private const string NativeScenePath = "assets/scenes/fixture.scene.json";

    // The document's one entity and its one tile, plus a glyph per character of the label: a font
    // that generated nothing, or a page that did not ship, draws fewer than this.
    private const int DocumentSprites = 2;

    private const int MinimumVisible = DocumentSprites + FixtureLabel.Glyphs;

    private const string DevelopmentTexturePath = "assets/textures/development/scratch.png";

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    // Every check names both directions of its axis: a hook that stopped excluding and one that
    // started excluding everything must both fail here rather than downstream. Each answers with its
    // own exit code so a failure names the axis.
    private static int Run(string[] args)
    {
        bool shipping = !Development.IsSupported;
        bool published = !RuntimeFeature.IsDynamicCodeSupported;

        // A NativeAOT binary is the publish, and a publish ships: this is what holds CI's run to the
        // shipping direction of every check below.
        if (published && !shipping)
        {
            Console.Error.WriteLine(
                "AOT smoke failed (3): this is a published binary whose Development.IsSupported is true, so the publish did not set CapsuleShipping.");

            return 3;
        }

        bool keptDriver = Registers(SmokeDrivers.Kept);
        bool developmentDriver = Registers(SmokeDrivers.DevelopmentOnly);

        if (!keptDriver || developmentDriver == shipping)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (4): the driver registry holds [{string.Join(", ", SmokeDrivers.Names)}]; it must register {SmokeDrivers.Kept} in every build, and {SmokeDrivers.DevelopmentOnly}, which sits under a .capsuleignore marker, in a non-shipping build only (shipping {shipping})."));

            return 4;
        }

        // The generated sprite for the module-derived sheet, referenced so a key that stopped
        // reaching the build tool is a compile error rather than a silent gap.
        Sprite module = CapsuleAssets.Sprites.Smoke.Module.Frames.Only;
        bool keptTexture = Shipped(CapsuleAssets.Textures.Kept) && module.Texture.Name == CapsuleAssets.Textures.Kept.Name;
        bool developmentTexture = File.Exists(Path.Combine(AppContext.BaseDirectory, DevelopmentTexturePath));

        if (!keptTexture || developmentTexture == shipping)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (5): the textures beside this executable must hold {CapsuleAssets.Textures.Kept.Name} (present {keptTexture}), and {DevelopmentTexturePath} (present {developmentTexture}) in a non-shipping build only (shipping {shipping})."));

            return 5;
        }

        // Twice in one process: each run builds a fresh store, so the second read comes back from
        // the file the first run wrote.
        HeadlessRunResult result = Play(args);
        int? firstRead = FixtureScene.RunsRead;
        HeadlessRunResult second = Play(args);
        int? secondRead = FixtureScene.RunsRead;

        bool contentShipped = ContentShipped();
        bool booted =
            result.Steps == DrivenSteps &&
            result.ExitRequested &&
            result.Metrics.Visible >= MinimumVisible &&
            second.Steps == DrivenSteps &&
            contentShipped &&
            firstRead == 0 &&
            secondRead == 1;

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (2): {result.Steps}/{DrivenSteps} steps, exit {result.ExitRequested}, {result.Metrics.Visible}/{result.Metrics.Submitted} commands (at least {MinimumVisible} visible), content {contentShipped}, runs read {firstRead} then {secondRead} (expected 0 then 1)."));

            return 2;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"AOT smoke passed (shipping {shipping}): {result.Steps} steps, {result.Metrics.Visible}/{result.Metrics.Submitted} commands, content shipped, runs read {firstRead} then {secondRead}."));

        return 0;
    }

    private static bool Registers(string driverName) => Array.IndexOf(SmokeDrivers.Names, driverName) >= 0;

    private static HeadlessRunResult Play(string[] args) =>
        CapsuleEngine.Configure("Capsule AOT Smoke", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithCommandLine(args)
            .WithRunStart(FixtureInput.Configure)
            .WithSampling(TextureSampling.Point)
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<FixtureScene>(new InputScript().Wait(IdleSteps).Tap(Key.Escape).Build());

    private static SceneDocument Document(string path) =>
        SceneDocumentFile.Load(Path.Combine(AppContext.BaseDirectory, path));

    // Assets/Textures/TileSets/Cave_Wall.png is spelled one way and keyed another, and the document
    // names it under the authored spelling: this is where the whole key path is proved end to end.
    private static bool ContentShipped()
    {
        SceneDocument fixture = Document(NativeScenePath);

        return fixture.Source is { Tool: "native" }
            && Shipped(CapsuleAssets.Textures.Pixel)
            && Shipped("fonts", "menu", ".png")

            // Compiled into the game, so it is not beside the executable.
            && !Shipped("fonts", "menu", ".fnt")
            && Shipped(CapsuleAssets.Textures.TileSets.CaveWall)
            && CapsuleAssets.Textures.TileSets.CaveWall.Name == "tile-sets/cave-wall"
            && fixture.Entries[1].TileMap?.Grid.Texture?.Name == "tile-sets/cave-wall";
    }

    private static bool Shipped(TextureHandle texture) => Shipped("textures", texture.Name, texture.Extension);

    private static bool Shipped(string domain, string name, string extension) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", domain, name + extension));
}
