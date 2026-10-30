using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Capsule.AotSmoke.Logic;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Platform;
using Capsule.Scenes;

namespace Capsule.AotSmoke;

internal static class Program
{
    private const int IdleSteps = 60;

    private const long DrivenSteps = IdleSteps + 1;

    private const string NativeScenePath = "assets/scenes/fixture.scene.json.gz";

    // The document's one entity and its one tile, plus a glyph per character of the label: a font
    // that generated nothing, or a page that did not ship, draws fewer than this.
    private const int DocumentSprites = 2;

    private const int MinimumVisible = DocumentSprites + FixtureLabel.Glyphs;

    private const string DevelopmentTexturePath = "assets/textures/development/scratch.png";

    // SHA-256 of the menu font page's premultiplied texels, as the texture store uploads them.
    private const string FontPageTexelsSha256 = "4F13FA9BDBEB25D496632C2E31133CF54C61C2593323F881F6B114384BAF7D4E";

    private const string SavesFlag = "--saves";

    // The warm-up grows every buffer the shove path keeps. The lift's round trip is 64 steps, and the
    // measured steps cover two.
    private const int ShoveWarmupSteps = 120;

    private const int ShoveSteps = 128;

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    // The runs check reads 0 then 1. The saves directory must start without a runs save. A run that
    // names no directory gets a fresh one of its own, deleted at the end. A named directory must be
    // missing or empty, and one holding anything fails here instead of at check 2.
    private static int Run(string[] args)
    {
        int flag = Array.IndexOf(args, SavesFlag);
        if (flag >= 0)
        {
            // A --saves with no directory after it is left for the engine's command line to refuse.
            string? given = flag + 1 < args.Length ? args[flag + 1] : null;
            if (given is not null && Directory.Exists(given) && Directory.EnumerateFileSystemEntries(given).Any())
            {
                Console.Error.WriteLine(
                    $"AOT smoke failed (8): the saves directory '{given}' is not empty, and the runs read cannot be trusted to start at 0. Pass an empty directory to {SavesFlag}, or omit {SavesFlag} to use a fresh temporary one.");

                return 8;
            }

            return Check(args);
        }

        DirectoryInfo saves = Directory.CreateTempSubdirectory("capsule-aot-smoke-");
        try
        {
            return Check([.. args, SavesFlag, saves.FullName]);
        }
        finally
        {
            saves.Delete(recursive: true);
        }
    }

    // Every check names both directions of its axis: a hook that stopped excluding and one that
    // started excluding everything must both fail here rather than downstream. Each answers with its
    // own exit code so a failure names the axis.
    private static int Check(string[] args)
    {
        bool shipping = !Development.IsSupported;
        bool published = !RuntimeFeature.IsDynamicCodeSupported;

        // A NativeAOT binary is the publish, and a publish ships: this is what holds CI's run to the
        // shipping direction of every check below.
        if (published && !shipping)
        {
            Console.Error.WriteLine(
                "AOT smoke failed (3): this is a published binary whose Development.IsSupported is true. Set CapsuleShipping on the publish.");

            return 3;
        }

        bool keptDriver = Registers(SmokeDrivers.Kept);
        bool developmentDriver = Registers(SmokeDrivers.DevelopmentOnly);

        if (!keptDriver || developmentDriver == shipping)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (4): the driver registry holds [{string.Join(", ", SmokeDrivers.Names)}]; it must register {SmokeDrivers.Kept} in every build, and {SmokeDrivers.DevelopmentOnly}, which sits under a .capsuleignore marker, in a non-shipping build only (shipping {shipping})."));

            return 4;
        }

        // The generated sprite for a sheet naming a texture in another folder, referenced so a key
        // that stopped reaching the build tool is a compile error rather than a silent gap.
        Sprite module = CapsuleAssets.Smoke.ModuleSheet.Frames.Only;
        bool keptTexture = Shipped(CapsuleAssets.Textures.KeptTexture) && module.Texture.Name == CapsuleAssets.Textures.KeptTexture.Name;
        bool developmentTexture = File.Exists(Path.Combine(AppContext.BaseDirectory, DevelopmentTexturePath));

        if (!keptTexture || developmentTexture == shipping)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (5): the textures beside this executable must hold {CapsuleAssets.Textures.KeptTexture.Name} (present {keptTexture}), and {DevelopmentTexturePath} (present {developmentTexture}) in a non-shipping build only (shipping {shipping})."));

            return 5;
        }

        // The half of a texture load that runs off the game thread needs no device. The published
        // decoder is proved here although the run below is headless.
        string texels = FontPageTexels();
        if (texels != FontPageTexelsSha256)
        {
            Console.Error.WriteLine($"AOT smoke failed (6): the menu font page decodes to texels hashing {texels}, not {FontPageTexelsSha256}.");

            return 6;
        }

        // Twice in one process. Each run builds a fresh store, and the second read comes back from
        // the file the first run wrote. The second asks for frame diagnostics, which a shipping build
        // ignores so that a trimmed publish drops them, and says so.
        string frames = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"capsule-aot-smoke-frames-{Environment.ProcessId}.csv"));
        HeadlessRunResult result = Play(args);
        int? firstRead = FixtureScene.RunsRead;
        WarningSink warnings = new();
        HeadlessRunResult second = Play(args, frames, warnings);
        int? secondRead = FixtureScene.RunsRead;
        bool wroteFrames = File.Exists(frames);
        File.Delete(frames);

        if (wroteFrames == shipping || warnings.Count != (shipping ? 1 : 0))
        {
            Console.Error.WriteLine($"AOT smoke failed (9): a run asking for frame diagnostics wrote them: {wroteFrames}, and warned of it {warnings.Count} times. A shipping build writes none and warns once, and any other build writes them without a warning (shipping {shipping}).");

            return 9;
        }

        // The fixture's beacon constructor read a private field and a generic base's member, both set through
        // generated [UnsafeAccessor] setters, and a value read through its [JsonConverter], off its entry. Its
        // OnStart read the entity reference the scene set once every entry was constructed.
        if (Beacon.Read != Beacon.Expected || !Beacon.Anchored)
        {
            Console.Error.WriteLine(
                $"AOT smoke failed (7): the fixture's beacon read its route, generic-base band and private charge as '{Beacon.Read}', not '{Beacon.Expected}', and its anchor reference was set: {Beacon.Anchored}.");

            return 7;
        }

        // A steady step allocates nothing. A sort given no comparison allocates on every call in this
        // build alone, and the shove path sorts on every move of a platform.
        (long shoveBytes, int shoves) = Shove();
        if (shoveBytes != 0 || shoves == 0)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (10): {ShoveSteps} steady steps of a platform shoving a body allocated {shoveBytes} bytes over {shoves} shoves. A steady step allocates nothing, and the steps must shove at least once."));

            return 10;
        }

        // A crash log names its build. Trimming must keep the version attributes it reads, and the
        // executable it hashes is this one.
        string build = BuildIdentity.Describe("Capsule AOT Smoke");
        if (build.Contains("unknown", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"AOT smoke failed (11): a crash log's header could not read part of the build:{Environment.NewLine}{build}");

            return 11;
        }

        bool contentShipped = ContentShipped();
        bool booted =
            result.Steps == DrivenSteps &&
            result.ExitRequested &&
            result.Metrics.Visible >= MinimumVisible &&
            second.Steps == DrivenSteps &&
            contentShipped &&
            firstRead == 0 &&
            secondRead == 1;

        if (!booted)
        {
            Console.Error.WriteLine(
                FormattableString.Invariant(
                    $"AOT smoke failed (2): {result.Steps}/{DrivenSteps} steps, exit {result.ExitRequested}, {result.Metrics.Visible}/{result.Metrics.Submitted} commands (at least {MinimumVisible} visible), content {contentShipped}, runs read {firstRead} then {secondRead} (expected 0 then 1)."));

            return 2;
        }

        Console.WriteLine(
            FormattableString.Invariant(
                $"AOT smoke passed (shipping {shipping}): {result.Steps} steps, {result.Metrics.Visible}/{result.Metrics.Submitted} commands, content shipped, runs read {firstRead} then {secondRead}."));

        return 0;
    }

    private static string FontPageTexels()
    {
        using Stream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "menu.png"));

        using DecodedTexture decoded = TextureDecoder.Decode(file, "menu");

        return Convert.ToHexString(SHA256.HashData(decoded.Texels));
    }

    private static (long Bytes, int Shoves) Shove()
    {
        // Two bodies meet the lift together, because a sort leaves a span of one alone.
        Scene scene = new();
        scene.Add(new ShoveLift(Vector2.Zero));
        ShovedBody body = new(new Vector2(40f, 0f));
        scene.Add(body);
        scene.Add(new ShovedBody(new Vector2(40f, 2f)));

        using SimulationHost host = new(scene);
        host.Step(ShoveWarmupSteps);
        int shovesBefore = body.Shoves;
        long before = GC.GetAllocatedBytesForCurrentThread();
        host.Step(ShoveSteps);

        return (GC.GetAllocatedBytesForCurrentThread() - before, body.Shoves - shovesBefore);
    }

    private static bool Registers(string driverName) => Array.IndexOf(SmokeDrivers.Names, driverName) >= 0;

    private static HeadlessRunResult Play(string[] args, string? frames = null, ILogSink? warnings = null)
    {
        EngineBuilder engine = CapsuleBoot.Configure("Capsule AOT Smoke")
            .WithCommandLine(args)
            .WithGame(static run => new FixtureGame(run))
            .WithSampling(TextureSampling.Point)
            .WithoutCrashLog()
            .WithoutLogging();

        if (frames is not null)
        {
            engine = engine.WithFrameDiagnostics(frames);
        }

        if (warnings is not null)
        {
            engine = engine.WithLogSink(warnings);
        }

        return engine.RunHeadless<FixtureScene>(new InputScript().Wait(IdleSteps).Tap(Key.Escape).Build());
    }

    // Assets/Textures/TileSets/Cave_Wall.png is spelled one way and keyed another. The document names it
    // under the authored spelling, and the run composing it resolves the key: the whole key path is proved here.
    private static bool ContentShipped()
    {
        using StreamReader inflated = new(new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, NativeScenePath)), CompressionMode.Decompress));
        SceneDocument fixture = SceneDocument.Parse(inflated.ReadToEnd());

        return fixture.Entries.Length > 0
            && Shipped(CapsuleAssets.Textures.PixelTexture)
            && Shipped("fonts/menu.png")

            // Compiled into the game and not beside the executable.
            && !Shipped("fonts/menu.fnt")
            && Shipped(CapsuleAssets.Textures.TileSets.CaveWallTexture)
            && CapsuleAssets.Textures.TileSets.CaveWallTexture.Name == "textures/tile-sets/cave-wall"
            && fixture.Entries[1].Members?.GetProperty("texture").GetString() == "Textures/TileSets/Cave_Wall.png";
    }

    private static bool Shipped(TextureHandle texture) => Shipped(texture.Name + texture.Extension);

    private static bool Shipped(string path) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", path));

    // Counts the warnings naming frame diagnostics. Another warning the run logs passes by.
    private sealed class WarningSink : ILogSink
    {
        public int Count { get; private set; }

        public void Write(LogLevel level, string message)
        {
            if (level == LogLevel.Warning && message.Contains("frame diagnostics", StringComparison.Ordinal))
            {
                Count++;
            }
        }
    }
}
