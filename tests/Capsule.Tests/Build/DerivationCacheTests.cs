using Capsule.Build.Caching;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// The derivation cache across runs over one output directory: what a run reuses, what it derives
/// again, and that a reused derivation hands on exactly what running it would.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class DerivationCacheTests
{
    private const string Hero = "Assets/Textures/hero.png";

    private const string Room = "Assets/Scenes/room.scene.json";

    private const string Font = """
        info face="Test" size=12
        common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=1 packed=0
        page id=0 file="menu.png"
        char id=65 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 xadvance=5 page=0 chnl=15
        char id=66 x=4 y=0 width=4 height=6 xoffset=0 yoffset=2 xadvance=6 page=0 chnl=15
        kerning first=65 second=66 amount=-2
        """;

    // Every cached kind, so the generated code a reused run renders is rendered from every kind's kept facts.
    [Fact]
    public void ARunOverAnUnchangedTree_DerivesNothingAndGeneratesWhatTheFirstRunDid()
    {
        using ToolWorkspace workspace = Small();
        workspace.WriteGreyPng("Assets/glow.png", 4, 3);
        workspace.Write("Assets/glow.png.config.json", """{ "format": "r8" }""");
        workspace.WritePng("Assets/Sprites/packed.png", 4, 3);
        workspace.Write("Assets/Sprites/packed.png.config.json", """{ "atlas": "game" }""");
        workspace.Write("Assets/game.atlas.json", "{}");
        workspace.Write("Assets/Audio/hum.wav", AudioProbeFixtures.Wav(1, 16, 8000, 800, loop: (100, 700)));
        workspace.Write("Assets/Sprites/prop.sheet.json", """{ "formatVersion": 1, "texture": "sprites/packed.png", "sockets": [ { "name": "hand" } ], "frames": [ { "name": "a", "x": 0, "y": 0, "width": 2, "height": 2, "pivot": [1, 0.5], "sockets": { "hand": [0, 1.5] } } ], "clips": [ { "name": "idle", "loop": true, "frames": [ { "frame": "a", "ticks": 6 } ] } ] }""");
        workspace.Write("Assets/Fonts/menu.fnt", Font);
        workspace.WritePng("Assets/Fonts/menu.png", 8, 8);
        workspace.Write("Assets/Shaders/tint.fx", "float Amount;\nfloat4 Fragment(SpritePixel pixel)\n{\n    return pixel.Texel * pixel.Tint * Amount;\n}\n");
        workspace.Succeed();
        string generated = workspace.Generated;
        string[] shipped = workspace.Shipped;

        workspace.Succeed();

        Assert.Empty(workspace.Built);
        Assert.Equal(generated, workspace.Generated);
        Assert.Equal(shipped, workspace.Shipped);
    }

    [Fact]
    public void AnEditedSource_IsTheOnlyDerivationRunAgain()
    {
        using ToolWorkspace workspace = Small();
        workspace.Succeed();

        workspace.Write(Room, """{"entities": []}""");
        workspace.Succeed();

        Assert.Equal(["scenes: " + Room], workspace.Built);
    }

    // A derivation whose shipped file went missing is run again, whatever its entry says.
    [Fact]
    public void AReusedOutputDeletedSinceTheLastRun_IsDerivedAgain()
    {
        using ToolWorkspace workspace = Small();
        workspace.Succeed();

        File.Delete(ToolWorkspace.Out + "/assets/textures/hero.png");
        workspace.Succeed();

        Assert.Equal(["textures: " + Hero], workspace.Built);
        Assert.Contains("textures/hero.png", workspace.Shipped);
    }

    // A checkout rewrites write times and leaves the bytes alone.
    [Fact]
    public void ATouchThatChangesNoByte_DerivesNothing()
    {
        using ToolWorkspace workspace = Small();
        workspace.Succeed();

        foreach (string file in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
        }

        workspace.Succeed();

        Assert.Empty(workspace.Built);
    }

    // An edit within the tick the last run started in keeps the file's length and write time. Only a
    // file written strictly earlier than that start keeps its recorded hash.
    [Fact]
    public void AnEditNoEarlierThanTheLastRunsStart_IsHashedAgain()
    {
        using ToolWorkspace workspace = Small();
        workspace.Succeed();
        DateTime written = File.GetLastWriteTimeUtc(Room);
        workspace.Write(Room, File.ReadAllText(Room).Replace("\"hp\": 3", "\"hp\": 4", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(Room, written);

        Started(written.AddTicks(1));
        workspace.Succeed();
        Assert.Empty(workspace.Built);

        Started(written);
        workspace.Succeed();
        Assert.Equal(["scenes: " + Room], workspace.Built);
    }

    [Fact]
    public void AChangedBuildOfTheDerivingCode_DerivesEverythingAgain()
    {
        using ToolWorkspace workspace = Small();
        string tool = "first";
        workspace.Configure = build =>
        {
            build.IdentifyTool = _ => tool;

            return build;
        };
        workspace.Succeed();
        string[] built = workspace.Built;

        tool = "second";
        workspace.Succeed();

        Assert.Equal(built, workspace.Built);
    }

    // A failed derivation leaves nothing that would let the next run skip it. The run still records
    // every derivation that succeeded.
    [Fact]
    public void AFailedDerivation_LeavesNoEntryAndIsAttemptedAgain()
    {
        using ToolWorkspace workspace = Small();
        workspace.Write("Assets/Audio/hum.wav", "not a wav");
        workspace.Fail();

        string errors = workspace.Fail();

        Assert.Contains("Assets/Audio/hum.wav", errors, StringComparison.Ordinal);
        Assert.Empty(workspace.Built);
        Assert.DoesNotContain("audio", Cache().Derivations.Keys);
    }

    // A texture and a scene, each shipping one file.
    private static ToolWorkspace Small()
    {
        ToolWorkspace workspace = new();
        workspace.WritePng(Hero, 4, 3);
        workspace.Write(Room, """{"entities": [ { "id": 1, "type": "crate", "x": 0, "y": 0, "hp": 3 } ]}""");

        return workspace;
    }

    private static DerivationCacheJson Cache()
    {
        using FileStream file = File.OpenRead(Path.Combine(ToolWorkspace.Out, DerivationCache.FileName));

        return System.Text.Json.JsonSerializer.Deserialize(file, DerivationCacheJsonContext.Default.DerivationCacheJson)!;
    }

    // Rewrites the start the last run recorded.
    private static void Started(DateTime started)
    {
        DerivationCacheJson cache = Cache();
        cache.Started = started;
        using FileStream file = File.Create(Path.Combine(ToolWorkspace.Out, DerivationCache.FileName));
        System.Text.Json.JsonSerializer.Serialize(file, cache, DerivationCacheJsonContext.Default.DerivationCacheJson);
    }
}
