using Capsule.Assets;
using Capsule.Audio;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.DeviceTests;

public sealed class PrefetchTransitionTests
{
    private static readonly TextureHandle PageA = new("page-a", ".png");

    private static readonly TextureHandle PageB = new("page-b", ".png");

    private static readonly AudioClip Hum = new("hum", ".wav", 1.0);

    // The arena's files are deleted before it is requested. Only prefetched media can reach it.
    [DeviceFact]
    public void APrefetchedTransition_ComposesWithEveryPageAndClipResident()
    {
        using Content content = new();
        bool arrived = false;
        SceneRegistry registry = new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(Lobby), _ => new Lobby(content)),
                SceneRegistration.Plain(typeof(Arena), _ => new Arena(() => arrived = true)),
            ]);

        DeviceThread.Run(() => CapsuleEngine.Configure("Prefetch", content, registry)
            .WithSaveDirectory(Path.Combine(content.Root, "saves"))
            .WithoutCrashLog()
            .WithoutLogging()
            .RunScene<Lobby>());

        Assert.True(arrived, "the run never reached the prefetched scene");
    }

    private sealed class Lobby(Content content) : Scene
    {
        protected override void OnStart() => Run.PrefetchScene<Arena>();

        protected override void OnStep(in StepContext context)
        {
            if (context.Tick == 90)
            {
                content.DeleteMedia();
                Run.RequestScene<Arena>();
            }
        }
    }

    private sealed class Arena(Action arrived) : Scene
    {
        protected override void CollectAssets(AssetCollection assets)
        {
            assets.Add([PageA, PageB]);
            assets.Add(Hum);
        }

        protected override void OnStart() => arrived();

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }

    private sealed class Content : HostPlatform, IDisposable
    {
        internal Content()
        {
            Root = Path.Combine(Path.GetTempPath(), "capsule-prefetch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "assets", "textures"));
            Directory.CreateDirectory(Path.Combine(Root, "assets", "audio"));
            File.Copy(Shipped("Pngs", "bench", "hd-page-a.png"), Media("textures", "page-a.png"));
            File.Copy(Shipped("Pngs", "bench", "hd-page-b.png"), Media("textures", "page-b.png"));
            File.Copy(Shipped("Media", "hum.wav"), Media("audio", "hum.wav"));
        }

        internal string Root { get; }

        public override Stream OpenContent(string relativePath) => File.OpenRead(Path.Combine(Root, relativePath));

        public override ISaveStorage OpenSaveStorage(string localFolderName) => throw new NotSupportedException();

        internal void DeleteMedia() => Directory.Delete(Path.Combine(Root, "assets"), recursive: true);

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static string Shipped(params string[] parts) => Path.Combine([AppContext.BaseDirectory, .. parts]);

        private string Media(string domain, string file) => Path.Combine(Root, "assets", domain, file);
    }
}
