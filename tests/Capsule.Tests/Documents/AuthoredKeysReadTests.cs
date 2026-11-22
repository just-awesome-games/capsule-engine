using System.Text.Json;
using Capsule.Assets;
using Capsule.Scenes;
using Capsule.Tests.Allocation;

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
        SceneDocumentEntry entry = new("thing", members);
        AuthoredMembers read = new(new AuthoredMembers.Composition(new SceneDocument([entry]), [], new AssetCollection()), entry, 0);
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

    // The entries of one composition note what they asked for together, and each failure names only its own entry's
    // keys, in the order its reads asked for them.
    [Fact]
    public void AnEntryOfAComposition_NamesOnlyTheKeysItsOwnReadsAskedFor()
    {
        SceneDocumentEntry first = new("thing", JsonDocument.Parse("""{"a": 1, "b": 2}""").RootElement);
        SceneDocumentEntry second = new("other", JsonDocument.Parse("""{"a": 1, "b": 2, "c": 3}""").RootElement);
        AuthoredMembers.Composition composition = new(new SceneDocument([first, second]), [], new());
        AuthoredMembers firstRead = new(composition, first, 0);
        AuthoredMembers secondRead = new(composition, second, 1);
        foreach (string key in (string[])["a", "absent", "b"])
        {
            _ = firstRead.Has(key);
        }

        foreach (string key in (string[])["a", "b", "absent"])
        {
            _ = secondRead.Has(key);
        }

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(composition.Finish);
        Assert.Equal(
            "entities[1] ('other') sets 'c', which no authorable member takes. Its authorable members are: a, b, absent. Correct the key, or mark the member [Authorable].",
            error.Message);
    }

    // A composition reports its first defect in document order, whether an entry's reference or a later entry's
    // unread key, or the other way round.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ACompositionWithTwoDefectiveEntries_ReportsTheEarlierOne(int linking)
    {
        JsonElement members = JsonDocument.Parse("""{"a": 1}""").RootElement;
        SceneDocumentEntry[] entries = [new("thing", members), new("other", members)];
        AuthoredMembers.Composition composition = new(new SceneDocument(entries), [], new());
        AuthoredMembers[] reads = [new(composition, entries[0], 0), new(composition, entries[1], 1)];
        _ = reads[linking].Has("a");
        reads[linking].Link(this, static (_, _) => throw new SceneDocumentFormatException("the reference"));

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(composition.Finish);
        Assert.StartsWith(linking == 0 ? "the reference" : "entities[0] ('thing') sets 'a'", error.Message, StringComparison.Ordinal);
    }

    // A composition whose reads asked for every key it authors finishes without allocating.
    [Fact]
    public void ACompositionWhoseReadsAskedForEveryKey_FinishesWithoutAllocating()
    {
        _ = FinishAllocates();

        Assert.Equal(0, FinishAllocates());
    }

    private static long FinishAllocates()
    {
        SceneDocumentEntry entry = new("thing", JsonDocument.Parse("""{"a": 1, "b": 2}""").RootElement);
        AuthoredMembers.Composition composition = new(new SceneDocument([entry]), [], new());
        AuthoredMembers read = new(composition, entry, 0);
        _ = read.Has("a");
        _ = read.Has("b");

        long before = StepMeasurement.Start();
        composition.Finish();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
