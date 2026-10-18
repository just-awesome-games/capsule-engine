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
}
