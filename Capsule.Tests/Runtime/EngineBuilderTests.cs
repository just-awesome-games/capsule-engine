using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

/// <summary>
/// Builder validation only: anything past <c>Run</c> needs a window and a graphics
/// device, which belongs to the verify harness rather than a unit spec.
/// </summary>
public sealed class EngineBuilderTests
{
    [Fact]
    public void Configure_StartsAFreshBuilder()
    {
        Assert.NotSame(CapsuleEngine.Configure(), CapsuleEngine.Configure());
    }

    [Fact]
    public void EveryWithMethod_ReturnsTheBuilderSoTheyChain()
    {
        EngineBuilder builder = CapsuleEngine.Configure();

        Assert.Same(builder, builder.WithWindow("Title", 320, 240, resizable: true));
        Assert.Same(builder, builder.WithFixedStep(120));
        Assert.Same(builder, builder.WithClearColor(ColorRgba.CornflowerBlue));
        Assert.Same(builder, builder.WithCrashLog("Game"));
        Assert.Same(builder, builder.WithBindings(bindings => bindings.Bind(new InputAction("Quit"), Key.Escape)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithWindow_RejectsAnUnnamedWindow(string? title)
    {
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure().WithWindow(title!, 1280, 720));
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    [InlineData(-1, 720)]
    [InlineData(1280, -1)]
    public void WithWindow_RejectsNonPositiveDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure().WithWindow("Title", width, height));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void WithFixedStep_RejectsANonPositiveRate(int hertz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure().WithFixedStep(hertz));
    }

    // The character cases are Windows-invalid but legal on POSIX: they must be rejected
    // wherever the suite runs, or a Linux CI pass would clear a name that breaks a player.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("C:name")]
    [InlineData("Game?")]
    [InlineData("Game*")]
    [InlineData("Game<1>")]
    [InlineData("Game|Pipe")]
    [InlineData("Game\"Quoted\"")]
    [InlineData("Game\tTab")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Game.")]
    [InlineData("Game ")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Com1")]
    [InlineData("LPT9")]
    [InlineData("AUX.log")]
    public void WithCrashLog_RejectsAnythingThatIsNotOneSafeDirectoryName(string? appName)
    {
        // Each of these either escapes %LOCALAPPDATA%/<appName> or, on Windows, resolves
        // to something other than a directory.
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure().WithCrashLog(appName!));
    }

    // Code points rather than literals: a raw control character in the source would be
    // invisible to a reader and to a diff.
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0x1F)]
    public void WithCrashLog_RejectsAControlCharacter(int codePoint)
    {
        Assert.Throws<ArgumentException>(
            () => CapsuleEngine.Configure().WithCrashLog($"Game{(char)codePoint}Name"));
    }

    [Theory]
    [InlineData("Game")]
    [InlineData("X Plus")]
    [InlineData("JAG.Studios.XPlus")]
    [InlineData("CONsole")]
    [InlineData("COM10")]
    public void WithCrashLog_AcceptsAnOrdinaryApplicationName(string appName)
    {
        CapsuleEngine.Configure().WithCrashLog(appName);
    }

    [Fact]
    public void WithBindings_RequiresAConfigurator()
    {
        Assert.Throws<ArgumentNullException>(() => CapsuleEngine.Configure().WithBindings(null!));
    }

    [Fact]
    public void Run_RequiresASimulation()
    {
        Assert.Throws<ArgumentNullException>(() => CapsuleEngine.Configure().Run(null!));
    }
}
