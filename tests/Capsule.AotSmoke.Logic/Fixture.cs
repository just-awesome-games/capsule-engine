using System.Numerics;
using System.Text.Json.Serialization;
using Capsule;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.UI;

namespace Capsule.AotSmoke.Logic;

// How many times the smoke has run over one saves directory: the document a second process reads
// back from the first, which is the whole persistence path under NativeAOT.
public sealed record RunCounter(int Runs);

// The game's own source-generated context, declared beside its keys; the store never sees it.
[JsonSerializable(typeof(RunCounter))]
public sealed partial class SmokeJsonContext : JsonSerializerContext;

public static class SmokeSaves
{
    public static readonly SaveKey<RunCounter> Runs = new("runs", SmokeJsonContext.Default.RunCounter, new RunCounter(0));
}

public static class FixtureInput
{
    public static readonly InputAction Quit = new("quit");

    public static void Configure(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Input.Bindings.Bind(Quit, Key.Escape);
    }
}

[SceneDocument("scenes/fixture")]
public sealed class FixtureScene(SceneContent content) : Scene(content)
{
    // What the last scene start read, for the shell to check after the run: a fixture's static.
    public static int? RunsRead { get; private set; }

    protected override void OnStart()
    {
        Camera.ViewportSize = new Vector2(16f, 16f);
        Add(new FixtureLabel(new Vector2(-6f, -6f)));

        RunCounter counter = Run.Saves.Read(SmokeSaves.Runs);
        RunsRead = counter.Runs;
        Run.Saves.Write(SmokeSaves.Runs, new RunCounter(counter.Runs + 1));
    }

    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(FixtureInput.Quit))
        {
            Run.RequestExit();
        }
    }
}

// Two glyphs of the generated font registry, drawn under NativeAOT: the whole font path from the
// build-read '.fnt' to a shipped page.
public sealed class FixtureLabel : Entity
{
    public const string Text = "AB";

    public const int Glyphs = 2;

    public FixtureLabel(Vector2 position)
        : base(position)
    {
        Add(new Label(CapsuleAssets.Fonts.MenuFont, Text));
    }
}

public sealed class FixtureEntity : Entity
{
    private static readonly Sprite Visual = new(CapsuleAssets.Textures.PixelTexture, new TextureRegion(0, 0, 1, 1));

    public FixtureEntity(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new SpriteRenderer(Visual));
    }
}
