using Capsule.AotSmoke.Logic;
using Capsule.Assets;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Generated;
using Capsule.Scenes.Input;

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

    public static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run()
    {
        IInputDriver driver = new InputScript()
            .Wait(IdleSteps)
            .Tap(Key.Escape)
            .Build();

        HeadlessRunResult result = CapsuleEngine.Configure("Capsule AOT Smoke", CapsuleScenes.Registry)
            .WithInput(FixtureInput.Configure)
            .WithSampling(TextureSampling.Point)
            .WithoutCrashLog()
            .WithoutLogging()
            .RunHeadless<FixtureScene>(driver);

        bool contentShipped = ContentShipped();
        bool booted =
            result.Steps == DrivenSteps &&
            result.ExitRequested &&
            result.Metrics.Visible >= MinimumVisible &&
            contentShipped;

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed: {result.Steps}/{DrivenSteps} steps, exit {result.ExitRequested}, {result.Metrics.Visible}/{result.Metrics.Submitted} commands (at least {MinimumVisible} visible), content {contentShipped}."));
            return 1;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"AOT smoke passed: {result.Steps} steps, {result.Metrics.Visible}/{result.Metrics.Submitted} commands, content shipped."));
        return 0;
    }

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
