using System.Collections.Immutable;
using System.Reflection;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

// A scene preloads the CapsuleAssets members the code of its scene, entity and component classes reads, through
// the generated module initializer, with no registry and no host.
[Collection(LogSinkCollection.Name)]
public sealed class ClassAssetTests : IDisposable
{
    private const string Source = $$"""
        {{CapsuleAssetsStandIn.Source}}

        namespace Game
        {
            using System.Numerics;
            using Capsule.Assets;
            using Capsule.Audio;
            using Capsule.Scenes;

            public sealed class Room : Scene
            {
                public Room() => Add(new Avatar());

                private TextureHandle Backdrop => CapsuleAssets.Textures.NorthTexture;
            }

            public sealed class Avatar : Entity
            {
                public Avatar()
                    : base(Vector2.Zero) => Add(new Badge());

                private AudioClip Voice => CapsuleAssets.Audio.HumSound;
            }

            public sealed class Badge : Component
            {
                private TextureHandle Art => CapsuleAssets.Textures.SouthTexture;
            }

            public class Walker() : Entity(Vector2.Zero)
            {
                private AudioClip Step => CapsuleAssets.Audio.HumSound;
            }

            public class Runner : Walker
            {
                private AudioClip Dash => CapsuleAssets.Audio.ChimeSound;
            }

            public class Box<T>() : Entity(Vector2.Zero)
            {
                private TextureHandle Lid => CapsuleAssets.Textures.NorthTexture;
            }

            public sealed class Crate : Box<int>;

            [ManualPreloads]
            public class Quiet : Walker
            {
                private AudioClip Dash => CapsuleAssets.Audio.ChimeSound;
            }

            public sealed class Loud : Quiet
            {
                private TextureHandle Art => CapsuleAssets.Textures.SouthTexture;
            }

            public sealed class Spark() : Entity(Vector2.Zero)
            {
                private TextureHandle Art => CapsuleAssets.Textures.NorthTexture;
            }

            public static class Pools
            {
                public static void TakeSpark() => new EntityPool<Spark>(static () => new Spark(), capacity: 1).Take();
            }
        }
        """;

    private static readonly Lazy<Assembly> Game = new(static () => GeneratorHarness.Loaded(Compiled()));

    public void Dispose() => Log.UseSink(null);

    [Fact]
    public void ANewScene_PreloadsWhatItsSceneEntityAndComponentClassesRead()
    {
        Scene room = (Scene)Activator.CreateInstance(Game.Value.GetType("Game.Room")!)!;

        Assert.Equal(["textures/north", "textures/south", "audio/hum"], Names(room.CollectPreloads()));
    }

    // Each class records only its own reads, and the walk up its bases adds theirs. A generic base is recorded by
    // its definition.
    [Theory]
    [InlineData("Game.Runner", new[] { "audio/chime", "audio/hum" })]
    [InlineData("Game.Crate", new[] { "textures/north" })]
    public void AnEntity_PreloadsWhatItsBaseClassesRead(string type, string[] preloaded) =>
        Assert.Equal(preloaded, Names(EntityPreloads(type)));

    // Quiet is marked and reads the chime. Its base still preloads, and so does its unmarked subclass.
    [Fact]
    public void ManualPreloads_SilencesOnlyTheClassItMarks() =>
        Assert.Equal(["textures/south", "audio/hum"], Names(EntityPreloads("Game.Loud")));

    // The first take of a pool nothing forwarded probes what its entities would preload, which their class names.
    [Fact]
    public void APoolsFirstTake_SeesWhatItsEntitiesClassReads()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        Game.Value.GetType("Game.Pools")!.GetMethod("TakeSpark")!.Invoke(null, null);

        Assert.Single(sink.Entries, static entry => entry.Message.Contains("EntityPool<Spark>", StringComparison.Ordinal));
    }

    private static Compilation Compiled()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile(Source);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return updated;
    }

    private static AssetCollection EntityPreloads(string type)
    {
        Scene scene = new();
        scene.Add((Entity)Activator.CreateInstance(Game.Value.GetType(type)!)!);

        return scene.CollectPreloads();
    }

    private static string[] Names(AssetCollection assets) =>
        [.. assets.Textures.Select(static texture => texture.Name), .. assets.Clips.Select(static clip => clip.Name)];
}
