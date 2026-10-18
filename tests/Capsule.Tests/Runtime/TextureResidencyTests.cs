using Capsule.Assets;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// How scene-owned assets load and leave memory.
public sealed class TextureResidencyTests
{
    private static readonly TextureHandle Hero = new("hero", ".png");

    private static readonly TextureHandle Tiles = new("tiles", ".png");

    [Fact]
    public void ChangingScene_ReusesSharedAssetsAndReleasesOldPreloadsAndLazyLoads()
    {
        TextureHandle sharedHandle = new("shared", ".png");
        TextureHandle nextHandle = new("next", ".png");
        using SceneAssetStore<TextureHandle, DisposalProbe> store = SyncStore.Over<TextureHandle, DisposalProbe>(
            static handle => new DisposalProbe(handle.Name));
        store.ChangeScene([Hero, sharedHandle]);
        DisposalProbe hero = store.Get(Hero);
        DisposalProbe shared = store.Get(sharedHandle);
        DisposalProbe lazy = store.Get(Tiles);

        store.ChangeScene([sharedHandle, nextHandle]);

        Assert.True(hero.Disposed);
        Assert.True(lazy.Disposed);
        Assert.False(shared.Disposed);
        Assert.Same(shared, store.Get(sharedHandle));
        Assert.Equal("next", store.Get(nextHandle).Name);
    }

    // Pins that a transition leaks nothing: the store releases the outgoing scene's texture as it
    // takes the incoming one. Residency after the first transition never grows.
    [Fact]
    public void AHundredTransitionsBetweenTwoScenes_LeaveResidencyWhereTheFirstTransitionPutIt()
    {
        List<DisposalProbe> loaded = [];
        using SceneAssetStore<TextureHandle, DisposalProbe> store = SyncStore.Over<TextureHandle, DisposalProbe>(handle =>
        {
            DisposalProbe texture = new(handle.Name);
            loaded.Add(texture);

            return texture;
        });

        store.ChangeScene([Hero]);
        store.ChangeScene([Tiles]);
        int resident = loaded.Count(texture => !texture.Disposed);

        for (int transition = 0; transition < 100; transition++)
        {
            store.ChangeScene([transition % 2 == 0 ? Hero : Tiles]);
        }

        Assert.Equal(1, resident);
        Assert.Equal(resident, loaded.Count(texture => !texture.Disposed));
        Assert.Equal(102, loaded.Count);
    }

    [Fact]
    public void APreloadFailure_RetainsPriorAssetsAndDisposesStagedAssets()
    {
        TextureHandle stagedHandle = new("staged", ".png");
        DisposalProbe? staged = null;
        bool fail = false;
        using SceneAssetStore<TextureHandle, DisposalProbe> store = SyncStore.Over<TextureHandle, DisposalProbe>(handle =>
        {
            if (fail && handle == Tiles)
            {
                throw new InvalidDataException("decode failed");
            }

            DisposalProbe loaded = new(handle.Name);
            if (handle == stagedHandle)
            {
                staged = loaded;
            }

            return loaded;
        });
        store.ChangeScene([Hero]);
        DisposalProbe hero = store.Get(Hero);
        DisposalProbe lazy = store.Get(new TextureHandle("lazy", ".png"));
        fail = true;

        Assert.Throws<InvalidDataException>(
            () => store.ChangeScene([stagedHandle, Tiles]));

        Assert.Same(hero, store.Get(Hero));
        Assert.Same(lazy, store.Get(new TextureHandle("lazy", ".png")));
        Assert.False(hero.Disposed);
        Assert.False(lazy.Disposed);
        Assert.True(staged!.Disposed);
    }

    [Fact]
    public void AFailureInAnotherStore_RollsBackAllStagedAssets()
    {
        DisposalProbe? staged = null;
        using SceneAssetStore<TextureHandle, DisposalProbe> textures = SyncStore.Over<TextureHandle, DisposalProbe>(handle =>
        {
            DisposalProbe texture = new(handle.Name);
            if (handle == Tiles)
            {
                staged = texture;
            }

            return texture;
        });
        using SceneAssetStore<string, DisposalProbe> sounds = SyncStore.Over<string, DisposalProbe>(name =>
            name == "broken" ? throw new InvalidDataException("audio decode failed") : new DisposalProbe(name));
        DisposalProbe hero = textures.Get(Hero);
        DisposalProbe sound = sounds.Get("current");

        Assert.Throws<InvalidDataException>(() =>
            textures.ChangeScene([Tiles], () => sounds.ChangeScene(["broken"])));

        Assert.Same(hero, textures.Get(Hero));
        Assert.Same(sound, sounds.Get("current"));
        Assert.False(hero.Disposed);
        Assert.False(sound.Disposed);
        Assert.True(staged!.Disposed);

        textures.ChangeScene([Tiles], () => sounds.ChangeScene(["next"]));

        Assert.True(hero.Disposed);
        Assert.True(sound.Disposed);
        Assert.False(textures.Get(Tiles).Disposed);
        Assert.False(sounds.Get("next").Disposed);
    }

    [Fact]
    public void Dispose_ReleasesEveryAssetOwnedByTheScene()
    {
        SceneAssetStore<TextureHandle, DisposalProbe> store = SyncStore.Over<TextureHandle, DisposalProbe>(
            static handle => new DisposalProbe(handle.Name));
        store.ChangeScene([Hero]);
        DisposalProbe preload = store.Get(Hero);
        DisposalProbe lazy = store.Get(Tiles);

        store.Dispose();

        Assert.True(preload.Disposed);
        Assert.True(lazy.Disposed);
    }
}
