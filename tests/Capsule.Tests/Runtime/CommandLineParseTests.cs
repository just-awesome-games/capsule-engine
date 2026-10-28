using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;

namespace Capsule.Tests.Runtime;

// What the command line takes and refuses as it parses, before any run starts.
public sealed class CommandLineParseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("2.5")]
    public void Frames_TakesItsDurationFromExitAfter(string? seconds)
    {
        string[] frames = seconds is null ? ["--frames", "frames.csv"] : ["--frames", "frames.csv", "--exit-after", seconds];

        CommandLine parsed = CommandLine.Parse([.. frames, "--headless"], "Game", development: true);

        Assert.Equal("frames.csv", parsed.FramesPath);
        Assert.Equal(seconds is null ? null : 2.5, parsed.ExitAfterSeconds);
        Assert.True(parsed.Headless);
    }

    // A windowed replay plays at the run's pace unless it is told to fast-forward.
    [Fact]
    public void FastForward_IsTakenBesideAWindowedReplay()
    {
        Assert.False(CommandLine.Parse(["--replay", "run.recording"], "Game", development: true).FastForward);
        Assert.True(CommandLine.Parse(["--replay", "run.recording", "--fast-forward"], "Game", development: true).FastForward);
    }

    [Fact]
    public void Trace_TakesItsTicksAndNamesAsCommaLists()
    {
        CommandLine parsed = CommandLine.Parse(
            ["--replay", "run.recording", "--headless", "--trace", "trace.csv", "0,100..160,200..", "Avatar (1),*"], "Game", development: true);

        Assert.Equal("trace.csv", parsed.TracePath);
        Assert.Equal([new TickRange(0, 0), new TickRange(100, 160), new TickRange(200, long.MaxValue)], parsed.TraceTicks!);
        Assert.Equal(["Avatar (1)", "*"], parsed.TraceNames!);
    }

    [Fact]
    public void Capture_TakesItsDirectoryAndTicks_AndForceLiftsTheCeilings()
    {
        CommandLine parsed = CommandLine.Parse(
            ["--replay", "run.recording", "--headless", "--capture", "frames", "0,100..160", "--force"], "Game", development: true);

        Assert.Equal("frames", parsed.CapturePath);
        Assert.Equal([new TickRange(0, 0), new TickRange(100, 160)], parsed.CaptureTicks!);
        Assert.True(parsed.Force);
    }

    // The recording supplies the run's opening, seed and language, and a flag setting one would be ignored.
    [Fact]
    public void Replay_WithAFlagTheRecordingSupplies_IsRefusedNamingIt()
    {
        CommandLineException refused = Assert.Throws<CommandLineException>(
            () => CommandLine.Parse(["--replay", "run.recording", "--seed", "42"], "Game", development: true));

        Assert.Contains("--seed", refused.Message, StringComparison.Ordinal);
        Assert.Contains("the recording supplies", refused.Message, StringComparison.Ordinal);
    }

    // A shipping build declares no development flag. A player cannot drive, probe, reroute, reseed or
    // force the language of the game from its command line. The portable-saves lever and help stay.
    [Theory]
    [InlineData("--scene", "Selected")]
    [InlineData("--payload", "Westward")]
    [InlineData("--driver", "Idler")]
    [InlineData("--headless")]
    [InlineData("--frames", "frames.csv")]
    [InlineData("--uncapped")]
    [InlineData("--fast-forward")]
    [InlineData("--exit-after", "5")]
    [InlineData("--seed", "42")]
    [InlineData("--language", "fr")]
    [InlineData("--replay", "run.recording")]
    [InlineData("--recordings", "runs")]
    [InlineData("--trace", "trace.csv", "1", "Avatar")]
    [InlineData("--capture", "frames", "1")]
    [InlineData("--force")]
    public void AShippingBuild_RefusesADevelopmentFlagAsUnknown(params string[] args)
    {
        CommandLineException refused = Assert.Throws<CommandLineException>(
            () => CommandLine.Parse(args, "Game", development: false));

        Assert.Contains("unknown option", refused.Message, StringComparison.Ordinal);
        Assert.False(refused.HelpRequested);
        Assert.Equal(2, refused.ExitCode);
    }

    [Fact]
    public void AShippingBuild_KeepsSavesGpuDriverAndHelp()
    {
        CommandLine parsed = CommandLine.Parse(["--saves", "portable", "--gpu-driver", "vulkan", "--help"], "Game", development: false);

        Assert.Equal("portable", parsed.SavesPath);
        Assert.Equal("vulkan", parsed.GpuDriver);
        Assert.True(parsed.HelpRequested);

        string usage = CommandLine.UsageFor("Game", development: false);
        Assert.Contains("--saves <dir>", usage, StringComparison.Ordinal);
        Assert.Contains("--gpu-driver <name>", usage, StringComparison.Ordinal);
        Assert.Contains("--help", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--driver", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--headless", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--scene", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--payload", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--frames", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--uncapped", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--fast-forward", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--exit-after", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--seed", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--language", usage, StringComparison.Ordinal);
    }
}
