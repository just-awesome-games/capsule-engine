using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Persistence;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class CommandLineTests : IDisposable
{
    private const string HallKey = "halls/hall";

    private readonly TempWorkspace _workspace = new(nameof(CommandLineTests));

    private readonly TextWriter _output = Console.Out;
    private readonly TextWriter _error = Console.Error;
    private readonly StringWriter _captured = new();

    public CommandLineTests() => Console.SetError(_captured);

    public void Dispose()
    {
        Console.SetOut(_output);
        Console.SetError(_error);
        _captured.Dispose();
        _workspace.Dispose();
    }

    [Fact]
    public void Headless_RunsTheNamedDriverToItsEnd()
    {
        Assert.Equal(0, Builder().WithCommandLine(["--headless", "--driver", "Idler"]).RunScene<Idle>());
    }

    [Fact]
    public void ADriverNoRegistryHolds_IsReportedWithTheNamesThatAreRegistered()
    {
        string reported = Refused(["--headless", "--driver", "Wanderer"]);

        Assert.Contains("Wanderer", reported, StringComparison.Ordinal);
        Assert.Contains("Idler, Presser", reported, StringComparison.Ordinal);
        Assert.Contains("--driver <Name>", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void Headless_WithNoDriver_IsReportedLikeABadArgument()
    {
        Assert.Contains("--driver", Refused(["--headless"]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.5")]
    public void Frames_TakesAnOptionalDuration(string? seconds)
    {
        string[] frames = seconds is null
            ? ["--frames", Path.Combine(_workspace.Root, "frames.csv")]
            : ["--frames", Path.Combine(_workspace.Root, "frames.csv"), seconds];

        Assert.Equal(0, Builder().WithCommandLine([.. frames, "--headless", "--driver", "Idler"]).RunScene<Idle>());
    }

    [Fact]
    public void Scene_BootsTheNamedClassRatherThanTheOneRunSceneNames()
    {
        int before = Selected.Openings;
        EngineBuilder builder = Builder().WithCommandLine(["--headless", "--driver", "Idler", "--scene", "Selected"]);

        Assert.Equal(nameof(Selected), builder.SceneOverride);
        Assert.Equal(0, builder.RunScene<Idle>());
        Assert.Equal(before + 1, Selected.Openings);
    }

    // A document no class claims has no class name to give, so its key is the only way to name it.
    [Fact]
    public void Scene_BootsADocumentByItsKey()
    {
        string documents = Path.Combine(_workspace.Root, "assets", "halls");
        Directory.CreateDirectory(documents);
        ShippedSceneDocument.Write(
            SceneDocumentFile.Parse("""{"formatVersion": 6, "entities": [], "nextEntityId": 1}"""),
            Path.Combine(documents, "hall" + ShippedSceneDocument.Extension));

        int before = Hall.Openings;
        EngineBuilder builder = Builder(new ContentPlatform(_workspace.Root))
            .WithCommandLine(["--headless", "--driver", "Idler", "--scene", HallKey]);

        Assert.Equal(HallKey, builder.SceneOverride);
        Assert.Equal(0, builder.RunScene<Idle>());
        Assert.Equal(before + 1, Hall.Openings);
    }

    // The class is looked up first. The document here is never shipped, so booting it would fail.
    [Fact]
    public void Scene_NamingBothAClassAndADocumentKey_BootsTheClass()
    {
        int before = Selected.Openings;
        EngineBuilder builder = Builder(documentKey: nameof(Selected))
            .WithCommandLine(["--headless", "--driver", "Idler", "--scene", nameof(Selected)]);

        Assert.Equal(nameof(Selected), builder.SceneOverride);
        Assert.Equal(0, builder.RunScene<Idle>());
        Assert.Equal(before + 1, Selected.Openings);
    }

    [Fact]
    public void ASceneNoRegistryHolds_IsReportedWithTheClassesAndKeysThatAreRegistered()
    {
        string reported = Refused(["--headless", "--driver", "Idler", "--scene", "Nowhere"]);

        Assert.Contains("Nowhere", reported, StringComparison.Ordinal);
        Assert.Contains(nameof(Selected), reported, StringComparison.Ordinal);
        Assert.Contains(HallKey, reported, StringComparison.Ordinal);
        Assert.Contains("--scene <Name>", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_PrintsTheUsageAndStops()
    {
        StringWriter stdout = new();
        Console.SetOut(stdout);

        CommandLineException asked = Assert.Throws<CommandLineException>(() => Builder().WithCommandLine(["--help"]));

        Assert.True(asked.HelpRequested);
        Assert.Equal(0, asked.Report());
        Assert.Contains("--headless", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("--saves <dir>", stdout.ToString(), StringComparison.Ordinal);
    }

    // The flag is applied as eagerly as --frames, so the headless run persists into the directory it
    // names: the document the scene wrote on its way out is there when the run returns.
    [Fact]
    public void Saves_IsHonouredByAHeadlessRun()
    {
        string saves = Path.Combine(_workspace.Root, "saves");

        Assert.Equal(0, Builder().WithCommandLine(["--saves", saves, "--headless", "--driver", "Idler", "--scene", "Saver"]).RunScene<Idle>());
        Assert.True(File.Exists(Path.Combine(saves, "visits.save.json")));
    }

    [Theory]
    [MemberData(nameof(MalformedCommandLines))]
    public void AMalformedCommandLine_IsReportedWithTheUsageAndExitsTwo(string[] args)
    {
        string reported = Refused(args);

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
        ["--saves"],
        ["--saves", "--headless"],
        ["--rewind", "Idler"],
        ["--headless", "--headless"],
    ];

    // A shipping build declares no development flag, so a player cannot drive, probe or reroute the
    // game from its command line; the portable-saves lever and help stay.
    [Theory]
    [InlineData("--scene", "Selected")]
    [InlineData("--driver", "Idler")]
    [InlineData("--headless")]
    [InlineData("--frames", "frames.csv")]
    [InlineData("--uncapped")]
    public void AShippingBuild_RefusesADevelopmentFlagAsUnknown(params string[] args)
    {
        CommandLineException refused = Assert.Throws<CommandLineException>(
            () => CommandLine.Parse(args, "Game", development: false));

        Assert.Contains("unknown option", refused.Message, StringComparison.Ordinal);
        Assert.False(refused.HelpRequested);
        Assert.Equal(2, refused.ExitCode);
    }

    [Fact]
    public void AShippingBuild_KeepsSavesAndHelp()
    {
        CommandLine parsed = CommandLine.Parse(["--saves", "portable", "--help"], "Game", development: false);

        Assert.Equal("portable", parsed.SavesPath);
        Assert.True(parsed.HelpRequested);

        string usage = CommandLine.UsageFor("Game", development: false);
        Assert.Contains("--saves <dir>", usage, StringComparison.Ordinal);
        Assert.Contains("--help", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--driver", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--headless", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--scene", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--frames", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--uncapped", usage, StringComparison.Ordinal);
    }

    // The defect and the usage block as the shell prints them, with the exit code it returns.
    private string Refused(string[] args)
    {
        CommandLineException refused = Assert.Throws<CommandLineException>(() => Builder().WithCommandLine(args));

        Assert.False(refused.HelpRequested);
        Assert.Equal(2, refused.Report());

        return Captured();
    }

    private string Captured() => _captured.ToString();

    // Every registry holds the class-free document HallKey. A documentKey adds a second one.
    private static EngineBuilder Builder(HostPlatform? platform = null, string? documentKey = null) =>
        CapsuleEngine.Configure(
                "Command Line Game",
                platform ?? new DesktopPlatform(),
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(Idle), static _ => new Idle()),
                        SceneRegistration.Plain(typeof(Exiting), static _ => new Exiting()),
                        SceneRegistration.Plain(typeof(Selected), static _ => new Selected()),
                        SceneRegistration.Plain(typeof(Saver), static _ => new Saver()),
                        SceneRegistration.DocumentOnly(HallKey, static content => new Hall(content!.Value)),
                        .. documentKey is null
                            ? Array.Empty<SceneRegistration>()
                            : [SceneRegistration.DocumentOnly(documentKey, static content => new Hall(content!.Value))],
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
        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }

    private sealed class Saver : Scene
    {
        private static readonly SaveKey<int> Visits = new("visits", SaveTestJsonContext.Default.Int32, 0);

        protected override void OnStep(in StepContext context)
        {
            Run.Saves.Write(Visits, Run.Saves.Read(Visits) + 1);
            Run.RequestExit();
        }
    }

    // A document no class claims, composed into this scene so a test can see it booted.
    private sealed class Hall(SceneContent content) : Scene(content)
    {
        internal static int Openings { get; private set; }

        protected override void OnStart() => Openings++;

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }

    // Registered but never named by a RunScene call, so only --scene can open it.
    private sealed class Selected : Scene
    {
        internal static int Openings { get; private set; }

        protected override void OnStart() => Openings++;

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }
}
