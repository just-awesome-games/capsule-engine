using System.Numerics;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

public sealed class ScrollFieldsTests
{
    // The band and the factor sit after the scale.
    [Fact]
    public void BandAndScrollFactor_RoundTrip()
    {
        const string Json = """{"entities":[{"type":"sky","x":8,"scrollFactor":[0,0]},{"type":"coin","scale":[2,2],"zIndex":0,"scrollFactor":[1,1]},{"type":"coin"}]}""";

        SceneDocument document = SceneDocumentFile.Parse(Json);

        Assert.Equal(new SceneDocumentEntry("sky", 8f, 0f, ScrollFactor: Vector2.Zero), document.Entries[0]);

        // An authored identity is a value, an absent field is none, and the two survive the round trip
        // as the different documents they are.
        Assert.Equal((0, Vector2.One), (document.Entries[1].ZIndex, document.Entries[1].ScrollFactor));
        Assert.Equal((null, null), (document.Entries[2].ZIndex, document.Entries[2].ScrollFactor));
        Assert.Equal(Json, SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void Parse_RejectsAScrollFactorThatIsNotTwoComponents()
    {
        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse("""{"entities": [{"type": "coin", "scrollFactor": [0.5, 1, 2]}]}"""));

        Assert.Contains("entities[0] has a scrollFactor of 3 components", error.Message, StringComparison.Ordinal);
    }
}
