using System.Numerics;
using Capsule.Assets;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

public sealed class SceneHostAssetTests
{
    private static readonly TextureHandle Hud = new("hud", ".png");

    private static readonly TextureHandle Shared = new("shared", ".png");

    private static readonly TextureHandle Bat = new("enemies/bat", ".png");

    [Fact]
    public void ATransition_PreparesTheIncomingScenesAssetsBeforeStoppingTheOutgoingScene()
    {
        List<string> order = [];

        Scene Resolve(in SceneTransition target) => target.SceneType == typeof(MenuTextures)
            ? new MenuTextures(order)
            : new ArenaTextures();

        using SceneHost host = new(ToScene<MenuTextures>(), Resolve, new Run());
        host.PrepareAssets = assets => order.Add("prepare:" + Names(assets.Textures));

        host.Step(Step(0));

        Assert.IsType<ArenaTextures>(host.Scene);
        Assert.Equal(["prepare:enemies/bat,shared", "menu.stop"], order);
    }

    [Fact]
    public void ATransitionWhoseAssetsCannotBePrepared_ReleasesTheResolvedSceneAndKeepsTheCurrentOne()
    {
        List<string> lifecycle = [];
        RejectedArenaTextures? resolved = null;

        Scene Resolve(in SceneTransition target)
        {
            if (target.SceneType == typeof(MenuTextures))
            {
                return new MenuTextures([]);
            }

            resolved = new RejectedArenaTextures(lifecycle);
            return resolved;
        }

        using SceneHost host = new(ToScene<MenuTextures>(), Resolve, new Run());
        host.PrepareAssets = static assets =>
        {
            if (assets.Textures.Count > 0)
            {
                throw new InvalidDataException("decode failed");
            }
        };

        Assert.Throws<InvalidDataException>(() => host.Step(Step(0)));

        Assert.IsType<MenuTextures>(host.Scene);
        Assert.NotNull(resolved);
        Assert.Empty(resolved.Entities.ToArray());
        Assert.Null(resolved.Entity.SceneOrNull);
        Assert.Equal(["component+", "entity+", "entity-", "component-"], lifecycle);
    }

    [Fact]
    public void DisposingTheHost_ReleasesItsSceneAssets()
    {
        SceneHost host = new(ToScene<HookScene>(), (in SceneTransition _) => new HookScene(), new Run());
        List<int> preparedCounts = [];
        host.PrepareAssets = assets => preparedCounts.Add(assets.Textures.Count);

        host.Dispose();

        Assert.Equal([0], preparedCounts);
    }

    private static string Names(IReadOnlyList<TextureHandle> handles) =>
        string.Join(",", handles.Select(static handle => handle.Name).Order(StringComparer.Ordinal));

    private sealed class MenuTextures(List<string> order) : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) =>
            assets.Add([Hud, Shared]);

        protected override void OnStep(in StepContext context) => Run.RequestScene<ArenaTextures>();

        protected override void OnStop() => order.Add("menu.stop");
    }

    private sealed class ArenaTextures : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) =>
            assets.Add([Shared, Bat]);
    }

    private sealed class RejectedArenaTextures : Scene
    {
        internal RejectedArenaTextures(List<string> lifecycle)
        {
            Entity = new LifecycleEntity(lifecycle);
            Entity.Add(new LifecycleComponent(lifecycle));
            Add(Entity);
        }

        protected internal override void CollectAssets(AssetCollection assets) =>
            assets.Add([Shared, Bat]);

        internal LifecycleEntity Entity { get; }

        protected override void OnStart() => throw new InvalidOperationException("A rejected scene must not start.");

        protected override void OnStop() => throw new InvalidOperationException("A rejected scene must not stop.");
    }

    private sealed class LifecycleEntity(List<string> lifecycle) : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene() => lifecycle.Add("entity+");

        protected internal override void OnRemovedFromScene() => lifecycle.Add("entity-");
    }

    private sealed class LifecycleComponent(List<string> lifecycle) : Component
    {
        protected internal override void OnAddedToScene() => lifecycle.Add("component+");

        protected internal override void OnRemovedFromScene() => lifecycle.Add("component-");
    }
}
