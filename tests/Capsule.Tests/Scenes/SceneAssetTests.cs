using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

public sealed class SceneAssetTests
{
    private static readonly TextureHandle SpriteTexture = new("actors/player", ".png");
    private static readonly TextureHandle AnimationA = new("actors/player-a", ".png");
    private static readonly TextureHandle AnimationB = new("actors/player-b", ".png");
    private static readonly TextureHandle SceneExtra = new("scene/extra", ".png");
    private static readonly TextureHandle EntityExtra = new("entity/extra", ".png");
    private static readonly TextureHandle ComponentExtra = new("component/extra", ".png");
    private static readonly TextureHandle Unused = new("unused", ".png");

    [Fact]
    public void AssetCollection_IsPureOrderedAndDeduplicated()
    {
        AssetCollection assets = new();

        assets.Add(SpriteTexture);
        assets.Add([AnimationA, SpriteTexture, AnimationB]);
        assets.Add(AnimationA);

        Assert.Equal([SpriteTexture, AnimationA, AnimationB], assets.Textures);
    }

    [Fact]
    public void ActualTilesSpritesAndCurrentAnimationFrames_AreCollected()
    {
        Scene scene = new(SceneFixtures.Content(SceneFixtures.Room(), SceneFixtures.Registry()));
        SpriteRenderer animated = new(default);
        SpriteAnimator animator = new(animated);
        animator.Play(new SpriteClip(
            [Frame(AnimationA), Frame(AnimationB), Frame(AnimationA)],
            [1, 1, 1]));

        scene.Add(new TestEntity(new SpriteRenderer(Frame(SpriteTexture)), animated, animator));

        Assert.Equal(
            [SceneFixtures.Atlas, SpriteTexture, AnimationA, AnimationB],
            scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void SceneEntityAndComponentDeclarations_AreAdditive()
    {
        DeclaringScene scene = new();
        scene.Add(new DeclaringEntity(
            new DeclaringComponent(),
            new SpriteRenderer(Frame(SpriteTexture))));

        Assert.Equal(
            [SceneExtra, EntityExtra, ComponentExtra, SpriteTexture],
            scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void DuplicateTexturesAcrossComponents_AppearOnce()
    {
        Scene scene = new();
        scene.Add(new TestEntity(
            new SpriteRenderer(Frame(SpriteTexture)),
            new SpriteRenderer(Frame(SpriteTexture))));

        Assert.Equal([SpriteTexture], scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void AnUnspawnedRegistryEntry_ContributesNoAssets()
    {
        EntityRegistry registry = new(
            [new EntityRegistration("unused", static spawn => new TestEntity(new SpriteRenderer(Frame(Unused))))]);
        Scene scene = new(SceneFixtures.Content(SceneFixtures.RoomWithoutTerrain(), registry));

        Assert.Empty(scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void ARendererInitializedOnStart_ContributesNoDefaultHandleBeforeStart()
    {
        Scene scene = new();
        scene.Add(new StartsAnimationEntity());

        Assert.Empty(scene.CollectAssetPreloads().Textures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AScenesPreloads_HoldAPooledEntitysTextureOnlyWhenItsOwnerForwardsThePool(bool forwarded)
    {
        Scene scene = new();
        scene.Add(new PoolOwner(forwarded));

        Assert.Equal(forwarded, scene.CollectPreloads().Contains(EntityExtra));
    }

    [Fact]
    public void APool_CollectsEveryEntityItBuilt_IdleTakenOrPastItsCapacity()
    {
        int built = 0;
        EntityPool<TestEntity> pool = new(
            () => new TestEntity(new SpriteRenderer(Frame(new TextureHandle($"pooled/{built++}", ".png")))),
            capacity: 1);
        pool.Take();
        pool.Take();

        AssetCollection assets = new();
        pool.CollectAssets(assets);

        Assert.Equal([new TextureHandle("pooled/0", ".png"), new TextureHandle("pooled/1", ".png")], assets.Textures);
    }

    // An entity that assigns a material after collection declares it, and its shader and every texture
    // set on it load with the scene rather than on the first draw.
    [Fact]
    public void AMaterialAnEntityDeclares_PreloadsItsShaderAndTextures()
    {
        Scene scene = new();
        scene.Add(new AssignsMaterialLater());

        AssetCollection preloads = scene.CollectAssetPreloads();

        Assert.Equal([AssignsMaterialLater.Palette.Shader], preloads.Shaders);
        Assert.Equal([EntityExtra], preloads.Textures);
    }

    [Fact]
    public void APlacementNoEntityClaims_StillFailsAsASpawn()
    {
        Assert.Throws<SpawnException>(() => new Scene(SceneFixtures.Content(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "wyvern", 0f, 0f)),
            SceneFixtures.Registry())));
    }

    private static Sprite Frame(TextureHandle texture) => new(texture, new TextureRegion(0, 0, 1, 1));

    private class TestEntity : Entity
    {
        internal TestEntity(params Component[] components)
            : base(Vector2.Zero)
        {
            foreach (Component component in components)
            {
                Add(component);
            }
        }
    }

    private sealed class DeclaringScene : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(SceneExtra);
    }

    private sealed class DeclaringEntity : TestEntity
    {
        internal DeclaringEntity(params Component[] components)
            : base(components)
        {
        }

        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(EntityExtra);
    }

    private sealed class PoolOwner(bool forwarded) : Entity(Vector2.Zero)
    {
        private readonly EntityPool<TestEntity> _pool = new(() => new TestEntity(new SpriteRenderer(Frame(EntityExtra))), capacity: 2);

        protected internal override void CollectAssets(AssetCollection assets)
        {
            if (forwarded)
            {
                _pool.CollectAssets(assets);
            }
        }
    }

    private sealed class AssignsMaterialLater : Entity
    {
        internal static readonly Material Palette = Palettes();

        internal AssignsMaterialLater()
            : base(Vector2.Zero)
        {
        }

        // It holds no renderer yet, as a stage that sets a tile map's material once started holds none.
        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(Palette);

        private static Material Palettes()
        {
            Material palette = new(new Shader("effects/palette", new ShaderParameter("Table", ShaderParameterKind.Texture)));
            palette.Set("Table", EntityExtra);
            return palette;
        }
    }

    private sealed class DeclaringComponent : Component
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Add(ComponentExtra);
    }

    private sealed class StartsAnimationEntity : Entity
    {
        private readonly SpriteAnimator _animator;
        private readonly SpriteClip _clip = new([Frame(AnimationA)], [1]);

        internal StartsAnimationEntity()
            : base(Vector2.Zero)
        {
            SpriteRenderer renderer = new(default);
            _animator = new SpriteAnimator(renderer);
            Add(renderer);
            Add(_animator);
        }

        protected internal override void OnStart() => _animator.Play(_clip);
    }
}
