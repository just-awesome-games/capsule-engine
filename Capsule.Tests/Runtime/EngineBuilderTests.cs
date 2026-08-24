using Capsule.Rendering;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

/// <summary>
/// Builder validation only: anything past <c>Run</c> needs a window and a graphics
/// device, which belongs to the verify harness rather than a unit spec.
/// </summary>
public sealed class EngineBuilderTests
{
    // Zero would divide to an infinite step rather than fail, so the guard is the only
    // thing between a misconfiguration and a loop that never advances.
    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void WithFixedStep_RejectsANonPositiveRate(int hertz)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure().WithFixedStep(hertz));
    }

    // NaN passes every comparison-based range guard and an infinite ceiling never binds —
    // either would reach the loop as a frame bound that silently does nothing.
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void WithSpikeClamp_RejectsANonFiniteCeiling(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CapsuleEngine.Configure().WithSpikeClamp(seconds));
    }

    // Same NaN hole on the float side: a NaN radius would ride into PadFilter and poison
    // every filtered axis.
    [Theory]
    [InlineData(float.NaN, 0.12f)]
    [InlineData(0.25f, float.NaN)]
    public void WithGamepadDeadzones_RejectsANaNRadius(float stick, float trigger)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CapsuleEngine.Configure().WithGamepadDeadzones(stick, trigger));
    }

    // A clamp below one step could never carry a whole step, so the simulation would fall
    // behind real time at any frame rate. Neither With call can see the other's value, which
    // leaves Run the only place the pair can be rejected at all.
    [Fact]
    public void Run_RejectsASpikeClampBelowTheFixedStep()
    {
        EngineBuilder builder = CapsuleEngine.Configure()
            .WithFixedStep(60)
            .WithSpikeClamp(1.0 / 120);

        Assert.Throws<InvalidOperationException>(() => builder.Run(new IdleSimulation()));
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
        Assert.ThrowsAny<ArgumentException>(() => CapsuleEngine.Configure().WithCrashLog(appName));
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
            () => CapsuleEngine.Configure().WithCrashLog($"Game{(char)codePoint}Name"));
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
        CapsuleEngine.Configure().WithCrashLog(appName);
    }

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
