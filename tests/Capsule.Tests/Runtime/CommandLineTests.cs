using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Input;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class CommandLineTests : IDisposable
{
    private static readonly InputTape Tape = new InputScript().Tap(Key.Space).Wait(3).Build();

    private readonly string _directory =
        Directory.CreateTempSubdirectory(nameof(CommandLineTests)).FullName;

    private readonly TextWriter _output = Console.Out;
    private readonly TextWriter _error = Console.Error;
    private readonly StringWriter _captured = new();

    public CommandLineTests() => Console.SetError(_captured);

    public void Dispose()
    {
        Console.SetOut(_output);
        Console.SetError(_error);
        _captured.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Record_WritesTheTapeTheHeadlessRunConsumed()
    {
        string recorded = Path.Combine(_directory, "recorded.tape");

        Assert.Equal(0, Builder().WithCommandLine(["--record", recorded, "--headless", WrittenTape()]).RunScene<Idle>());

        using FileStream file = File.OpenRead(recorded);
        Assert.Equal(Tape, InputTapeFile.Read(file));
    }

    [Fact]
    public void Headless_OfARecordedFile_RunsIt()
    {
        string recorded = Path.Combine(_directory, "recorded.tape");
        Builder().WithCommandLine(["--record", recorded, "--headless", WrittenTape()]).RunScene<Idle>();

        Assert.Equal(0, Builder().WithCommandLine(["--headless", recorded]).RunScene<Idle>());
    }

    [Fact]
    public void Headless_OfATapeThatExitsTheGame_Succeeds()
    {
        Assert.Equal(0, Builder().WithCommandLine(["--headless", WrittenTape()]).RunScene<Exiting>());
    }

    [Fact]
    public void Replay_ReadsItsTapeFileAtRunTime()
    {
        string malformed = Path.Combine(_directory, "malformed.tape");
        File.WriteAllBytes(malformed, [1, 2, 3, 4]);

        // The replay tape is read even though the headless tape is the one that runs, so the
        // malformed one is what decides the exit code.
        Assert.Equal(2, Builder().WithCommandLine(["--replay", malformed, "--headless", WrittenTape()]).RunScene<Idle>());
        Assert.Contains(malformed, Captured(), StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_OfAReadableTape_IsAccepted()
    {
        Assert.Equal(0, Builder().WithCommandLine(["--replay", WrittenTape(), "--headless", WrittenTape()]).RunScene<Idle>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.5")]
    public void Frames_TakesAnOptionalDuration(string? seconds)
    {
        string[] frames = seconds is null
            ? ["--frames", Path.Combine(_directory, "frames.csv")]
            : ["--frames", Path.Combine(_directory, "frames.csv"), seconds];

        Assert.Equal(0, Builder().WithCommandLine([.. frames, "--headless", WrittenTape()]).RunScene<Idle>());
    }

    [Fact]
    public void Help_PrintsTheUsageAndStops()
    {
        StringWriter stdout = new();
        Console.SetOut(stdout);

        Assert.Equal(0, Builder().WithCommandLine(["--help"]).RunScene<Idle>());
        Assert.Contains("--headless", stdout.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedCommandLines))]
    public void AMalformedCommandLine_IsReportedWithTheUsageAndExitsTwo(string[] args)
    {
        Assert.Equal(2, Builder().WithCommandLine(args).RunScene<Idle>());

        string reported = Captured();
        Assert.Contains("--record <tape>", reported, StringComparison.Ordinal);
        Assert.Contains("--frames <csv> [seconds]", reported, StringComparison.Ordinal);
    }

    public static TheoryData<string[]> MalformedCommandLines() =>
    [
        ["--record"],
        ["--record", "   "],
        ["--record", "--headless", "run.tape"],
        ["--frames", "frames.csv", "0"],
        ["--frames", "frames.csv", "-1"],
        ["--frames", "frames.csv", "NaN"],
        ["--frames", "frames.csv", "Infinity"],
        ["--rewind", "run.tape"],
        ["--headless", "run.tape", "--headless", "run.tape"],
    ];

    [Fact]
    public void AHeadlessTapeThatIsMissing_IsReportedLikeABadArgument()
    {
        string missing = Path.Combine(_directory, "absent.tape");

        Assert.Equal(2, Builder().WithCommandLine(["--headless", missing]).RunScene<Idle>());
        Assert.Contains(missing, Captured(), StringComparison.Ordinal);
    }

    private string Captured() => _captured.ToString();

    private string WrittenTape()
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.tape");

        using FileStream file = File.Create(path);
        InputTapeFile.Write(file, Tape);

        return path;
    }

    private static SceneEngineBuilder Builder() =>
        CapsuleEngine.Configure(
                "Command Line Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(Idle), static () => new Idle()),
                        SceneRegistration.Plain(typeof(Exiting), static () => new Exiting()),
                    ]))
            .WithFixedStep(10)
            .WithoutCrashLog()
            .WithoutLogging();

    private sealed class Idle : Scene;

    private sealed class Exiting : Scene
    {
        protected override void OnStep(in StepContext context) => RequestExit();
    }
}
