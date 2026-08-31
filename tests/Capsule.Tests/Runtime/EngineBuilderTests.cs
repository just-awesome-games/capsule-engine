using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

public sealed class EngineBuilderTests
{
    private const string GameName = "Spec Game";

    [Fact]
    public void WithFixedStep_RejectsANonPositiveRate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure(GameName).WithFixedStep(0));
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(320, 0)]
    public void WithRenderResolution_RejectsANonPositiveExtent(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure(GameName).WithRenderResolution(width, height));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void WithSpikeClamp_RejectsANonFiniteCeiling(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure(GameName).WithSpikeClamp(seconds));
    }

    [Theory]
    [InlineData(float.NaN, 0.12f)]
    [InlineData(0.25f, float.NaN)]
    public void WithGamepadDeadzones_RejectsANaNRadius(float stick, float trigger)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CapsuleEngine.Configure(GameName).WithGamepadDeadzones(stick, trigger));
    }

    [Fact]
    public void Run_RejectsASpikeClampBelowTheFixedStep()
    {
        SimulationEngineBuilder builder = CapsuleEngine.Configure(GameName)
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
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure(gameName));
    }

    [Theory]
    [InlineData("X Plus")]
    [InlineData("JAG.Studios.XPlus")]
    [InlineData("CONsole")]
    public void Configure_AcceptsAnOrdinaryGameName(string gameName)
    {
        CapsuleEngine.Configure(gameName);
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
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure(GameName).WithCrashLog(appName));
    }

    // The top of the control range: the row an off-by-one in the unsafe-character set lets through.
    [Fact]
    public void WithCrashLog_RejectsAControlCharacter()
    {
        Assert.Throws<ArgumentException>(
            () => CapsuleEngine.Configure(GameName).WithCrashLog($"Game{(char)0x1F}Name"));
    }

    [Theory]
    [InlineData("X Plus")]
    [InlineData("JAG.Studios.XPlus")]
    [InlineData("CONsole")]
    [InlineData("COM10")]
    public void WithCrashLog_AcceptsAnOrdinaryApplicationName(string appName)
    {
        CapsuleEngine.Configure(GameName).WithCrashLog(appName);
    }

    [Fact]
    public void WithWindowTitle_RejectsABlankTitle()
    {
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure(GameName).WithWindowTitle("  "));
    }

    [Theory]
    [InlineData(float.NaN, 180f)]
    [InlineData(320f, float.PositiveInfinity)]
    [InlineData(-320f, 180f)]
    [InlineData(320f, -180f)]
    public void WithCameraViewport_RejectsASpanThatIsNotFiniteAndNonNegative(float width, float height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SceneBuilder().WithCameraViewport(new Vector2(width, height)));
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
    [InlineData("scenes/room-01")]
    [InlineData("../room-01")]
    [InlineData("nul")]
    [InlineData("room-01 ")]
    public void RunScene_RejectsADocumentNameThatIsNotOneSafeFileName(string documentName)
    {
        SceneEngineBuilder builder = SceneBuilder()
            .WithRenderResolution(320, 180)
            .WithCameraViewport(new Vector2(320, 180))
            .WithSampling(TextureSampling.Point)
            .WithWindow(1280, 720)
            .WithoutCrashLog()
            .WithBindings(static _ => { });

        Assert.Throws<ArgumentException>(() => builder.RunScene(documentName));
    }

    private static SceneEngineBuilder SceneBuilder() =>
        CapsuleEngine.Configure(GameName, new SceneRegistry(new EntityRegistry([]), [MenuRegistration]));

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
