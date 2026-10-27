using System.IO.Compression;
using Capsule.Input;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Tests.Persistence;
using Capsule.Tests.Scenes;

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
        Assert.Equal(0, Builder().WithCommandLine(["--headless", "--driver", "Idler"]).RunScene<SceneFixtures.Idle>());
    }

    // A name no registry holds is reported with the flag's usage and every name that is registered. A
    // scene lists its classes and its document keys.
    [Theory]
    [InlineData(new[] { "--headless", "--driver", "Wanderer" }, new[] { "Wanderer", "Idler, Presser", "--driver <Name>" })]
    [InlineData(new[] { "--headless", "--driver", "Idler", "--scene", "Nowhere" }, new[] { "Nowhere", nameof(Selected), HallKey, "--scene <Name>" })]
    [InlineData(new[] { "--payload", "Northward" }, new[] { "'Northward'", "Eastward, Westward", "--payload <Name>" })]
    public void ANameNoRegistryHolds_IsReportedWithTheNamesThatAreRegistered(string[] args, string[] reported)
    {
        string report = Refused(args);

        Assert.All(reported, name => Assert.Contains(name, report, StringComparison.Ordinal));
    }

    // The driver is checked when the run starts. A driver set in code before or after the command
    // line plays a headless run.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Headless_PlaysADriverSetInCodeInEitherOrder(bool driverFirst)
    {
        IInputDriver driver = new InputScript().Wait(3).Build();
        EngineBuilder builder = driverFirst
            ? Builder().WithInputDriver(driver).WithCommandLine(["--headless"])
            : Builder().WithCommandLine(["--headless"]).WithInputDriver(driver);

        Assert.Equal(0, builder.RunScene<SceneFixtures.Idle>());
    }

    [Fact]
    public void Headless_WithNoDriver_IsReportedLikeABadArgument()
    {
        EngineBuilder builder = Builder().WithCommandLine(["--headless"]);

        CommandLineException refused = Assert.Throws<CommandLineException>(() => builder.RunScene<SceneFixtures.Idle>());

        Assert.Equal(2, refused.Report());
        Assert.Contains("--driver", _captured.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.5")]
    public void Frames_TakesAnOptionalDuration(string? seconds)
    {
        string[] frames = seconds is null ? ["--frames", "frames.csv"] : ["--frames", "frames.csv", seconds];

        CommandLine parsed = CommandLine.Parse([.. frames, "--headless"], "Game", development: true);

        Assert.Equal("frames.csv", parsed.FramesPath);
        Assert.Equal(seconds is null ? null : 2.5, parsed.FramesSeconds);
        Assert.True(parsed.Headless);
    }

    [Fact]
    public void Scene_BootsTheNamedClassRatherThanTheOneRunSceneNames()
    {
        List<object> opened = [];
        EngineBuilder builder = Builder(opened: opened).WithCommandLine(["--headless", "--driver", "Idler", "--scene", "Selected"]);

        Assert.Equal(nameof(Selected), builder.SceneOverride);
        Assert.Equal(0, builder.RunScene<SceneFixtures.Idle>());
        Assert.Equal<object>([nameof(Selected)], opened);
    }

    // A document no class claims has no class name to give. Its key is the only way to name it.
    [Fact]
    public void Scene_BootsADocumentByItsKey()
    {
        string documents = Path.Combine(_workspace.Root, "assets", "halls");
        Directory.CreateDirectory(documents);
        ShippedSceneDocument.Write(
            SceneDocument.Parse("""{"entities": []}"""),
            Path.Combine(documents, "hall" + ShippedSceneDocument.Extension),
            CompressionLevel.Fastest);

        List<object> opened = [];
        EngineBuilder builder = Builder(opened: opened)
            .WithPlatform(new ContentPlatform(_workspace.Root))
            .WithCommandLine(["--headless", "--driver", "Idler", "--scene", HallKey]);

        Assert.Equal(HallKey, builder.SceneOverride);
        Assert.Equal(0, builder.RunScene<SceneFixtures.Idle>());
        Assert.Equal<object>([nameof(Hall)], opened);
    }

    // The class is looked up first. The document here is never shipped. Booting it would fail.
    [Fact]
    public void Scene_NamingBothAClassAndADocumentKey_BootsTheClass()
    {
        List<object> opened = [];
        EngineBuilder builder = Builder(documentKey: nameof(Selected), opened: opened)
            .WithCommandLine(["--headless", "--driver", "Idler", "--scene", nameof(Selected)]);

        Assert.Equal(nameof(Selected), builder.SceneOverride);
        Assert.Equal(0, builder.RunScene<SceneFixtures.Idle>());
        Assert.Equal<object>([nameof(Selected)], opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Payload_ReplacesTheOpeningScenesPayload(bool withScene)
    {
        string[] args = ["--headless", "--driver", "Idler", "--payload", nameof(Westward)];
        List<object> opened = [];
        EngineBuilder builder = Builder(opened: opened).WithCommandLine(withScene ? [.. args, "--scene", nameof(Greeted)] : args);

        Assert.Equal(0, withScene ? builder.RunScene<SceneFixtures.Idle>(new CallsOwn()) : builder.RunScene<Greeted>(new CallsOwn()));
        Assert.IsType<Westward>(Assert.Single(opened));
    }

    [Fact]
    public void Seed_StartsEveryRunFromTheNamedSeed()
    {
        EngineBuilder builder = Builder().WithCommandLine(["--seed", "18446744073709551615"]);

        Assert.Equal(ulong.MaxValue, builder.RunSeed(played: true));
        Assert.Equal(ulong.MaxValue, builder.RunSeed(played: false));
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
        Assert.Contains("--payload <Name>", stdout.ToString(), StringComparison.Ordinal);
    }

    // The flag is applied as eagerly as --frames. The headless run persists into the directory it
    // names: the document the scene wrote on its way out is there when the run returns.
    [Fact]
    public void Saves_IsHonouredByAHeadlessRun()
    {
        string saves = Path.Combine(_workspace.Root, "saves");

        Assert.Equal(0, Builder().WithCommandLine(["--saves", saves, "--headless", "--driver", "Idler", "--scene", "Saver"]).RunScene<SceneFixtures.Idle>());
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
        ["--frames", "frames.csv", "NaN"],
        ["--payload"],
        ["--payload", "--headless"],
        ["--payload", "Westward", "--payload", "Eastward"],
        ["--seed", "-1"],
        ["--seed", "18446744073709551616"],
        ["--rewind", "Idler"],
        ["--headless", "--headless"],
        ["--gpu-driver"],
    ];

    // A shipping build declares no development flag. A player cannot drive, probe, reroute, reseed or
    // force the language of the game from its command line. The portable-saves lever and help stay.
    [Theory]
    [InlineData("--scene", "Selected")]
    [InlineData("--payload", "Westward")]
    [InlineData("--driver", "Idler")]
    [InlineData("--headless")]
    [InlineData("--frames", "frames.csv")]
    [InlineData("--uncapped")]
    [InlineData("--seed", "42")]
    [InlineData("--language", "fr")]
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
        Assert.DoesNotContain("--seed", usage, StringComparison.Ordinal);
        Assert.DoesNotContain("--language", usage, StringComparison.Ordinal);
    }

    // The defect and the usage block as the shell prints them, with the exit code it returns.
    private string Refused(string[] args)
    {
        CommandLineException refused = Assert.Throws<CommandLineException>(() => Builder().WithCommandLine(args));

        Assert.False(refused.HelpRequested);
        Assert.Equal(2, refused.Report());

        return _captured.ToString();
    }

    // Every registry holds the class-free document HallKey. A documentKey adds a second one. The
    // recording scenes add what they opened with to opened.
    private static EngineBuilder Builder(string? documentKey = null, List<object>? opened = null) =>
        CapsuleEngine.Configure(
                "Command Line Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(SceneFixtures.Idle), static _ => new SceneFixtures.Idle()),
                        SceneRegistration.Plain(typeof(Selected), _ => new Selected(opened)),
                        SceneRegistration.Plain(typeof(Saver), static _ => new Saver()),
                        SceneRegistration.Plain(typeof(Greeted), _ => new Greeted(opened)),
                        SceneRegistration.DocumentOnly(HallKey, content => new Hall(content!.Value, opened)),
                        .. documentKey is null
                            ? Array.Empty<SceneRegistration>()
                            : [SceneRegistration.DocumentOnly(documentKey, content => new Hall(content!.Value, opened))],
                    ]),
                new InputDriverRegistry(
                    [
                        new InputDriverRegistration("Idler", static () => new InputScript().Wait(3).Build()),
                        new InputDriverRegistration("Presser", static () => new InputScript().Tap(Key.Space).Build()),
                    ]),
                new ScenePayloadRegistry(
                    [
                        new ScenePayloadRegistration(nameof(Westward), static () => new Westward()),
                        new ScenePayloadRegistration(nameof(Eastward), static () => new Eastward()),
                    ]))
            .WithFixedStep(10)
            .WithoutCrashLog()
            .WithoutLogging();

    private sealed class Saver : Scene
    {
        private static readonly SaveKey<int> Visits = new("visits", SaveTestJsonContext.Default.Int32, 0);

        protected override void OnStep(in StepContext context)
        {
            Run.Saves.Write(Visits, Run.Saves.Read(Visits) + 1);
            Run.RequestExit();
        }
    }

    // A document no class claims, composed into this scene, which records that it booted.
    private sealed class Hall(SceneContent content, List<object>? opened) : Scene(content)
    {
        protected override void OnStart() => opened?.Add(nameof(Hall));

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }

    private sealed class Westward : IScenePayload;

    private sealed class Eastward : IScenePayload;

    private sealed record CallsOwn : IScenePayload;

    // Records the payload it opened with, which is what --payload delivered.
    private sealed class Greeted(List<object>? opened) : Scene
    {
        protected override void OnStart() => opened?.Add(EntryPayload!);

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }

    // Registered but never named by a RunScene call. Only --scene can open it.
    private sealed class Selected(List<object>? opened) : Scene
    {
        protected override void OnStart() => opened?.Add(nameof(Selected));

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }
}
