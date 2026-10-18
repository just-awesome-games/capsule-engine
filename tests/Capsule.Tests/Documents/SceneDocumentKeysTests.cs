using Capsule.Scenes;

namespace Capsule.Tests.Documents;

public sealed class SceneDocumentKeysTests
{
    // The generator refuses an [Authorable] member taking any of a layer's constants, and the document refuses
    // a document or entry member its layer's Contains names. The two lists must agree.
    [Theory]
    [InlineData(typeof(SceneDocumentKeys.Document))]
    [InlineData(typeof(SceneDocumentKeys.Entry))]
    public void EveryKeyConstant_IsOneItsLayerContains(Type layer)
    {
        Func<string, bool> contains = layer.GetMethod(nameof(SceneDocumentKeys.Entry.Contains))!.CreateDelegate<Func<string, bool>>();
        string[] keys = [.. layer.GetFields().Where(static field => field.IsLiteral).Select(static field => (string)field.GetRawConstantValue()!)];

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.True(contains(key), $"{layer.Name}.Contains refuses its own key '{key}'."));
    }
}
