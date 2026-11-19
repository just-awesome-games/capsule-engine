using System.Text.Json;
using Capsule.Scenes;

namespace Capsule.Tests.Documents;

// Once an owner is composed, a key no read asked for fails it. A read marks an authored key by its place among the
// object's keys, which runs past 64 in an object that authors that many.
public sealed class AuthoredKeysReadTests
{
    private const int Keys = 70;

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    [InlineData(66)]
    public void AKeyNoReadAskedFor_FailsTheOwner_NamingEveryKeyAsked(int? unread)
    {
        string[] keys = [.. Enumerable.Range(0, Keys).Select(static index => $"k{index}")];
        JsonElement members = JsonDocument.Parse("{" + string.Join(", ", keys.Select(static key => $"\"{key}\": 1")) + "}").RootElement;
        AuthoredMembers read = new(new SceneDocumentEntry("thing", members), 0);
        string[] asked = [.. keys.Where((_, index) => index != unread), "absent"];
        foreach (string key in asked)
        {
            _ = read.Has(key);
            _ = read.Has(key);
        }

        if (unread is null)
        {
            read.Finish();
            return;
        }

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(read.Finish);
        Assert.Equal(
            $"entities[0] ('thing') sets 'k{unread}', which no authorable member takes. Its authorable members are: {string.Join(", ", asked)}. Correct the key, or mark the member [Authorable].",
            error.Message);
    }
}
