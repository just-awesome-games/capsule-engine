using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class CommandLineTests : IDisposable
{
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
    public void Headless_RunsTheNamedDriverToItsEnd()
    {
        Assert.Equal(0, Builder().WithCommandLine(["--headless", "--driver", "Idler"]).RunScene<Idle>());
    }

    [Fact]
    public void Headless_OfADriverThatExitsTheGame_Succeeds()
    {
        Assert.Equal(0, Builder().WithCommandLine(["--headless", "--driver", "Idler"]).RunScene<Exiting>());
    }

    [Fact]
    public void ADriverNoRegistryHolds_IsReportedWithTheNamesThatAreRegistered()
    {
        Assert.Equal(2, Builder().WithCommandLine(["--headless", "--driver", "Wanderer"]).RunScene<Idle>());

        string reported = Captured();
        Assert.Contains("Wanderer", reported, StringComparison.Ordinal);
        Assert.Contains("Idler, Presser", reported, StringComparison.Ordinal);
        Assert.Contains("--driver <Name>", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void Headless_WithNoDriver_IsReportedLikeABadArgument()
    {
        Assert.Equal(2, Builder().WithCommandLine(["--headless"]).RunScene<Idle>());
        Assert.Contains("--driver", Captured(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.5")]
    public void Frames_TakesAnOptionalDuration(string? seconds)
    {
        string[] frames = seconds is null
            ? ["--frames", Path.Combine(_directory, "frames.csv")]
            : ["--frames", Path.Combine(_directory, "frames.csv"), seconds];

        Assert.Equal(0, Builder().WithCommandLine([.. frames, "--headless", "--driver", "Idler"]).RunScene<Idle>());
    }

    [Fact]
    public void Scene_BootsTheNamedClassRatherThanTheOneRunSceneNames()
    {
        int before = Selected.Openings;

        Assert.Equal(0, Builder().WithCommandLine(["--headless", "--driver", "Idler", "--scene", "Selected"]).RunScene<Idle>());
        Assert.Equal(before + 1, Selected.Openings);
    }

    [Fact]
    public void ASceneNoRegistryHolds_IsReportedWithTheScenesThatAreRegistered()
    {
        Assert.Equal(2, Builder().WithCommandLine(["--headless", "--driver", "Idler", "--scene", "Nowhere"]).RunScene<Idle>());

        string reported = Captured();
        Assert.Contains("Nowhere", reported, StringComparison.Ordinal);
        Assert.Contains(nameof(Selected), reported, StringComparison.Ordinal);
        Assert.Contains("--scene <Name>", reported, StringComparison.Ordinal);
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
        Assert.Contains("--driver <Name>", reported, StringComparison.Ordinal);
        Assert.Contains("--frames <csv> [seconds]", reported, StringComparison.Ordinal);
    }

    public static TheoryData<string[]> MalformedCommandLines() =>
    [
        ["--driver"],
        ["--driver", "   "],
        ["--driver", "--headless"],
        ["--frames", "frames.csv", "0"],
        ["--frames", "frames.csv", "-1"],
        ["--frames", "frames.csv", "NaN"],
        ["--frames", "frames.csv", "Infinity"],
        ["--scene"],
        ["--scene", "--headless"],
        ["--rewind", "Idler"],
        ["--headless", "--headless"],
    ];

    private string Captured() => _captured.ToString();

    private static EngineBuilder Builder() =>
        CapsuleEngine.Configure(
                "Command Line Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(Idle), static () => new Idle()),
                        SceneRegistration.Plain(typeof(Exiting), static () => new Exiting()),
                        SceneRegistration.Plain(typeof(Selected), static () => new Selected()),
                    ]),
                new InputDriverRegistry(
                    [
                        new InputDriverRegistration("Idler", static () => new InputScript().Wait(3).Build()),
                        new InputDriverRegistration("Presser", static () => new InputScript().Tap(Key.Space).Build()),
                    ]))
            .WithFixedStep(10)
            .WithoutCrashLog()
            .WithoutLogging();

    private sealed class Idle : Scene;

    private sealed class Exiting : Scene
    {
        protected override void OnStep(in StepContext context) => RequestExit();
    }

    // Registered but never named by a RunScene call, so only --scene can open it.
    private sealed class Selected : Scene
    {
        internal static int Openings { get; private set; }

        protected override void OnStart() => Openings++;

        protected override void OnStep(in StepContext context) => RequestExit();
    }
}
