using System.Runtime.CompilerServices;
using Capsule.AotSmoke.Logic;
using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
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

    private static int Run(string[] args)
    {
        // A NativeAOT binary is the publish, where the switch must be present and off; an ordinary
        // source-mode run has no shipping runtimeconfig at all.
        bool published = !RuntimeFeature.IsDynamicCodeSupported;
        bool developmentDisabled = AppContext.TryGetSwitch("Capsule.Development", out bool on) && !on;

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
            secondRead == 1 &&
            (!published || developmentDisabled);

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed: {result.Steps}/{DrivenSteps} steps, exit {result.ExitRequested}, {result.Metrics.Visible}/{result.Metrics.Submitted} commands (at least {MinimumVisible} visible), content {contentShipped}, runs read {firstRead} then {secondRead} (expected 0 then 1), development disabled {developmentDisabled}."));
            return 1;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"AOT smoke passed: {result.Steps} steps, {result.Metrics.Visible}/{result.Metrics.Submitted} commands, content shipped, runs read {firstRead} then {secondRead}."));
        return 0;
    }

    private static HeadlessRunResult Play(string[] args) =>
        CapsuleEngine.Configure("Capsule AOT Smoke", CapsuleScenes.Registry)
            .WithCommandLine(args)
            .WithInput(FixtureInput.Configure)
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
