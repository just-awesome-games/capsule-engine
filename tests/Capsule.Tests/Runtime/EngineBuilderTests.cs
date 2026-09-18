using System.Numerics;
using Capsule.Assets;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class EngineBuilderTests
{
    private const string GameName = "Spec Game";

    [Theory]
    [MemberData(nameof(BadSetterActions))]
    public void Setters_RejectBadValues(Action<EngineBuilder> badSetter)
    {
        Assert.ThrowsAny<ArgumentException>(() => badSetter(SceneBuilder()));
    }

    public static IEnumerable<object[]> BadSetterActions()
    {
        yield return [new Action<EngineBuilder>(b => b.WithFixedStep(0))];
        yield return [new Action<EngineBuilder>(b => b.WithRenderResolution(0, 180))];
        yield return [new Action<EngineBuilder>(b => b.WithRenderResolution(320, 0))];
        yield return [new Action<EngineBuilder>(b => b.WithCanvas(0, 360))];
        yield return [new Action<EngineBuilder>(b => b.WithCanvas(640, 0))];
        yield return [new Action<EngineBuilder>(b => b.WithMaxStepsPerFrame(0))];
        yield return [new Action<EngineBuilder>(b => b.WithMaxStepsPerFrame(-1))];
        yield return [new Action<EngineBuilder>(b => b.WithInput(i => i.GamepadDeadzones(float.NaN, 0.12f)))];
        yield return [new Action<EngineBuilder>(b => b.WithInput(i => i.GamepadDeadzones(0.25f, float.NaN)))];
        yield return [new Action<EngineBuilder>(b => b.WithWindowTitle("  "))];
        yield return [new Action<EngineBuilder>(b => b.WithSampling((TextureSampling)99))];
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("nul")]
    public void Configure_RejectsAGameNameThatNoSafeLocalFolderSlugsOutOf(string gameName)
    {
        Assert.ThrowsAny<ArgumentException>(() => SceneBuilder(gameName));
    }

    [Theory]
    [InlineData("bad\\name")]
    [InlineData("C:name")]
    [InlineData("..")]
    [InlineData("Game ")]
    [InlineData("nul")]
    [InlineData("AUX.log")]
    // The top of the control range: the row an off-by-one in the unsafe-character set lets through.
    [InlineData("Game\u001FName")]
    public void WithLocalFolder_RejectsAnythingThatIsNotOneSafeDirectoryName(string folderName)
    {
        Assert.ThrowsAny<ArgumentException>(() => SceneBuilder().WithLocalFolder(folderName));
    }

    // The canvas a run's screen layer is laid out in: declared, it is what the game said; otherwise
    // the render resolution stands in, and with neither the window the run opens at.
    [Fact]
    public void TheCanvas_IsTheDeclaredOneElseTheRenderResolutionElseTheWindow()
    {
        Assert.Equal(new Vector2(640f, 360f), CanvasOf(b => b.WithWindow(1280, 720).WithRenderResolution(320, 180).WithCanvas(640, 360)));
        Assert.Equal(new Vector2(320f, 180f), CanvasOf(b => b.WithWindow(1280, 720).WithRenderResolution(320, 180)));
        Assert.Equal(new Vector2(1280f, 720f), CanvasOf(b => b.WithWindow(1280, 720)));
    }

    // The canvas the run opened with, read by the scene as it starts on a one-step headless run.
    private static Vector2 CanvasOf(Func<EngineBuilder, EngineBuilder> configure)
    {
        Vector2 seen = Vector2.Zero;
        EngineBuilder builder = CapsuleEngine.Configure(
                GameName,
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(Reader), () => new Reader(canvas => seen = canvas))]))
            .WithoutCrashLog()
            .WithoutLogging();

        configure(builder).RunHeadless<Reader>(new InputScript().Wait(1).Build());

        return seen;
    }

    [Fact]
    public void RunScene_ForAClassTheRegistryDoesNotHold_NamesWhatItDoesHold()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => ConfiguredBuilder().RunScene<Room01>());

        Assert.Contains("Room01", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Menu", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scenes\\room-01")]
    [InlineData("../room-01")]
    [InlineData("nul")]
    [InlineData("room-01 ")]
    [InlineData("stage-1//room-01")]
    [InlineData("stage-1/../room-01")]
    [InlineData("rooms/room.one")]
    [InlineData("rooms/room one")]
    public void RunScene_RejectsADocumentNameThatIsNoSafePath(string documentName)
    {
        Assert.Throws<ArgumentException>(() => ConfiguredBuilder().RunScene(documentName));
    }

    private static EngineBuilder SceneBuilder(string gameName = GameName) =>
        CapsuleEngine.Configure(gameName, new SceneRegistry(new EntityRegistry([]), [MenuRegistration]));

    // Every setter a game reaches for, so a rejection above is the run's and not a half-built
    // builder's; silent logging is where a headless run starts.
    private static EngineBuilder ConfiguredBuilder() =>
        SceneBuilder()
            .WithWindowTitle("Spec")
            .WithWindow(1280, 720, resizable: false)
            .WithFullscreen()
            .WithRenderResolution(320, 180)
            .WithSampling(TextureSampling.Point)
            .WithRandomSeed(7)
            .WithoutCrashLog()
            .WithoutLogging()
            .WithInput(static input => input.GamepadDeadzones(0.25f, 0.12f));

    private static SceneRegistration MenuRegistration =>
        SceneRegistration.Plain(typeof(Menu), static () => new Menu());

    private sealed class Menu : Scene;

    private sealed class Reader(Action<Vector2> read) : Scene
    {
        protected override void OnStart() => read(Run.Canvas);
    }

    private sealed class Room01(SceneContent content) : Scene(content);
}
