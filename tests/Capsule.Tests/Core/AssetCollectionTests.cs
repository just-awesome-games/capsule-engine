using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Core;

public sealed class AssetCollectionTests
{
    // A collection gathered into another carries every preload it holds, shaders included.
    [Fact]
    public void AddingACollection_ForwardsItsShaders()
    {
        Shader tint = new("effects/tint", new ShaderParameter("Amount", ShaderParameterKind.Float));
        AssetCollection declared = new();
        declared.Add(new Material(tint));

        AssetCollection gathered = new();
        gathered.Add(declared);

        Assert.True(gathered.Contains(tint));
        Assert.Equal([tint], gathered.Shaders);
        Assert.True(gathered.HoldsPreloads);
    }

    // Each asset a class can read adds what loading it needs: a sprite its texture, a clip every frame's, a variant
    // every texture it draws from, a font its pages and a shader itself.
    [Fact]
    public void EachAssetOverload_AddsWhatItsAssetLoads()
    {
        TextureHandle body = new("figure/body", ".png");
        TextureHandle wave = new("figure/wave", ".png");
        TextureHandle hat = new("figure/hat", ".png");
        TextureHandle page = new("fonts/plain", ".png");
        Sprite idle = new(body, new TextureRegion(0, 0, 8, 8));
        Shader tint = new("effects/tint");
        AssetCollection assets = new();

        assets.Add(idle);
        assets.Add(new SpriteClip([idle, new Sprite(wave, new TextureRegion(0, 0, 8, 8))], [1, 1]));
        assets.Add(new SpriteVariant([idle], [new Sprite(hat, new TextureRegion(0, 0, 8, 8))]));
        assets.Add(new BitmapFont(8, 6, [page], [], []));
        assets.Add(tint);

        Assert.Equal([body, wave, hat, page], assets.Textures);
        Assert.Equal([tint], assets.Shaders);
    }
}
