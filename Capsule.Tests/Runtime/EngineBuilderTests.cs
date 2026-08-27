using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

/// <summary>
/// Builder validation only: anything past <c>Run</c> needs a window and a graphics
/// device, which belongs to the verify harness rather than a unit spec.
/// </summary>
public sealed class EngineBuilderTests
{
    private const string GameName = "Spec Game";

    // Zero would divide to an infinite step rather than fail, so the guard is the only
    // thing between a misconfiguration and a loop that never advances.
    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void WithFixedStep_RejectsANonPositiveRate(int hertz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure(GameName).WithFixedStep(hertz));
    }

    // A non-positive resolution reaches the host as a render-target size, which throws deep
    // inside device creation with nothing naming the call that caused it.
    [Theory]
    [InlineData(0, 180)]
    [InlineData(320, 0)]
    [InlineData(-320, -180)]
    public void WithRenderResolution_RejectsANonPositiveExtent(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure(GameName).WithRenderResolution(width, height));
    }

    // NaN passes every comparison-based range guard and an infinite ceiling never binds —
    // either would reach the loop as a frame bound that silently does nothing.
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void WithSpikeClamp_RejectsANonFiniteCeiling(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure(GameName).WithSpikeClamp(seconds));
    }

    // Same NaN hole on the float side: a NaN radius would ride into PadFilter and poison
    // every filtered axis.
    [Theory]
    [InlineData(float.NaN, 0.12f)]
    [InlineData(0.25f, float.NaN)]
    public void WithGamepadDeadzones_RejectsANaNRadius(float stick, float trigger)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CapsuleEngine.Configure(GameName).WithGamepadDeadzones(stick, trigger));
    }

    // A clamp below one step could never carry a whole step, so the simulation would fall
    // behind real time at any frame rate. Neither With call can see the other's value, which
    // leaves Run the only place the pair can be rejected at all.
    [Fact]
    public void Run_RejectsASpikeClampBelowTheFixedStep()
    {
        SimulationEngineBuilder builder = CapsuleEngine.Configure(GameName)
            .WithFixedStep(60)
            .WithSpikeClamp(1.0 / 120);

        Assert.Throws<InvalidOperationException>(() => builder.Run(new IdleSimulation()));
    }

    // The crash log is on by default under a folder slugged from the game's name, so a name no
    // safe folder comes out of has to fail here rather than at the moment the game crashes —
    // the one moment nothing is left to report it.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("nul")]
    [InlineData("CON")]
    public void Configure_RejectsAGameNameThatNoSafeCrashLogFolderSlugsOutOf(string gameName)
    {
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure(gameName));
    }

    // Separators and punctuation are ordinary in a display name and become one hyphen each;
    // only what survives has to be a directory name.
    [Theory]
    [InlineData("X Plus")]
    [InlineData("JAG.Studios.XPlus")]
    [InlineData("CONsole")]
    public void Configure_AcceptsAnOrdinaryGameName(string gameName)
    {
        CapsuleEngine.Configure(gameName);
    }

    // The separator and device cases are Windows-invalid but legal on POSIX: they must be
    // rejected wherever the suite runs, or a Linux CI pass would clear a name that breaks
    // a player. One row per rejection rule, not per character.
    [Theory]
    [InlineData("bad\\name")]
    [InlineData("C:name")]
    [InlineData("..")]
    [InlineData("Game ")]
    [InlineData("nul")]
    [InlineData("AUX.log")]
    public void WithCrashLog_RejectsAnythingThatIsNotOneSafeDirectoryName(string appName)
    {
        // Each of these either escapes %LOCALAPPDATA%/<appName> or, on Windows, resolves
        // to something other than a directory.
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure(GameName).WithCrashLog(appName));
    }

    // The ends of the control range: an off-by-one in the set builder loses one of them.
    // Code points rather than literals, since a raw control character in the source would
    // be invisible to a reader and to a diff.
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x1F)]
    public void WithCrashLog_RejectsAControlCharacter(int codePoint)
    {
        Assert.Throws<ArgumentException>(
            () => CapsuleEngine.Configure(GameName).WithCrashLog($"Game{(char)codePoint}Name"));
    }

    // The false-positive side: a device name is a whole stem, not a prefix, and an
    // interior space or dot is ordinary.
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

    // A NaN span passes every comparison-based guard and a negative one inverts the projection:
    // either reaches the renderer as a camera that quietly draws nothing.
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

    // Maps ship into one flat directory beside the executable, so a name that is a path or that
    // Windows resolves as a device would not name a file the build hook wrote. Reached through a
    // full chain, which is also what holds the registry-carrying type across every With: were any
    // of them to hand back the registry-free builder, RunScene would not compile here.
    [Theory]
    [InlineData("maps/room-01")]
    [InlineData("../room-01")]
    [InlineData("nul")]
    [InlineData("room-01 ")]
    public void RunScene_RejectsAMapNameThatIsNotOneSafeFileName(string mapName)
    {
        SceneEngineBuilder builder = SceneBuilder()
            .WithRenderResolution(320, 180)
            .WithCameraViewport(new Vector2(320, 180))
            .WithSampling(TextureSampling.Point)
            .WithWindow(1280, 720)
            .WithoutCrashLog()
            .WithBindings(static _ => { });

        Assert.Throws<ArgumentException>(() => builder.RunScene(mapName));
    }

    private static SceneEngineBuilder SceneBuilder() =>
        CapsuleEngine.Configure(GameName, new SceneRegistry(new EntityRegistry([]), [MenuRegistration]));

    private static SceneRegistration MenuRegistration =>
        SceneRegistration.Plain(typeof(Menu), static () => new Menu());

    private sealed class Menu : Scene;

    private sealed class Room01(MapSceneContext context) : MapScene(context);

    // Never stepped: Run rejects the configuration before it opens a window.
    private sealed class IdleSimulation : ISimulation
    {
        public bool ExitRequested => true;

        public FrameView View { get; } = new();

        public void Step(in StepContext context)
        {
        }
    }
}
