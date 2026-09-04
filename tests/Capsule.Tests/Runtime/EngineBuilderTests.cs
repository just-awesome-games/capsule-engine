using Capsule.Assets;
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
    public void Setters_RejectBadValues(Action<SceneEngineBuilder> badSetter)
    {
        Assert.ThrowsAny<ArgumentException>(() => badSetter(SceneBuilder()));
    }

    public static IEnumerable<object[]> BadSetterActions()
    {
        yield return [new Action<SceneEngineBuilder>(b => b.WithFixedStep(0))];
        yield return [new Action<SceneEngineBuilder>(b => b.WithRenderResolution(0, 180))];
        yield return [new Action<SceneEngineBuilder>(b => b.WithRenderResolution(320, 0))];
        yield return [new Action<SceneEngineBuilder>(b => b.WithSpikeClamp(double.NaN))];
        yield return [new Action<SceneEngineBuilder>(b => b.WithSpikeClamp(double.PositiveInfinity))];
        yield return [new Action<SceneEngineBuilder>(b => b.WithGamepadDeadzones(float.NaN, 0.12f))];
        yield return [new Action<SceneEngineBuilder>(b => b.WithGamepadDeadzones(0.25f, float.NaN))];
    }

    [Fact]
    public void Run_RejectsASpikeClampBelowTheFixedStep()
    {
        SceneEngineBuilder builder = SceneBuilder()
            .WithFixedStep(60)
            .WithSpikeClamp(1.0 / 120);

        Assert.Throws<InvalidOperationException>(() => builder.Run(new IdleSimulation()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("nul")]
    public void Configure_RejectsAGameNameThatNoSafeCrashLogFolderSlugsOutOf(string gameName)
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
    public void WithCrashLog_RejectsAnythingThatIsNotOneSafeDirectoryName(string appName)
    {
        Assert.ThrowsAny<ArgumentException>(() => SceneBuilder().WithCrashLog(appName));
    }

    // The top of the control range: the row an off-by-one in the unsafe-character set lets through.
    [Fact]
    public void WithCrashLog_RejectsAControlCharacter()
    {
        Assert.Throws<ArgumentException>(
            () => SceneBuilder().WithCrashLog($"Game{(char)0x1F}Name"));
    }

    [Fact]
    public void WithWindowTitle_RejectsABlankTitle()
    {
        Assert.ThrowsAny<ArgumentException>(() => SceneBuilder().WithWindowTitle("  "));
    }

    [Fact]
    public void WithSampling_RejectsAModeThatIsNotDeclared()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneBuilder().WithSampling((TextureSampling)99));
    }

    [Fact]
    public void RunScene_ForAClassTheRegistryDoesNotHold_NamesWhatItDoesHold()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => SceneBuilder().RunScene<Room01>());

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
        SceneEngineBuilder builder = SceneBuilder()
            .WithRenderResolution(320, 180)
            .WithSampling(TextureSampling.Point)
            .WithWindow(1280, 720)
            .WithoutCrashLog()
            .WithBindings(static _ => { });

        Assert.Throws<ArgumentException>(() => builder.RunScene(documentName));
    }

    private static SceneEngineBuilder SceneBuilder(string gameName = GameName) =>
        CapsuleEngine.Configure(gameName, new SceneRegistry(new EntityRegistry([]), [MenuRegistration]), []);

    private static SceneRegistration MenuRegistration =>
        SceneRegistration.Plain(typeof(Menu), static () => new Menu());

    private sealed class Menu : Scene;

    private sealed class Room01(SceneContent content) : Scene(content);

    private sealed class IdleSimulation : ISimulation
    {
        public bool ExitRequested => true;

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
        }
    }
}
