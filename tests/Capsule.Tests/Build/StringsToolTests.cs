using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using System.Text;
using Capsule.Build.Strings;
using Capsule.Localization;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using Capsule.Tests.Generators;
using Capsule.Tests.Rendering;
using Capsule.Tests.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Build;

/// <summary>
/// What the build reads out of string tables, how it holds each translation to the source language,
/// and the <c>CapsuleStrings</c> a game compiles against once it has.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class StringsToolTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        GeneratorHarness.Referenced(null, typeof(StringTable).Assembly, typeof(object).Assembly);

    // Each row: the files authored, the fallback the build project sets or null, and what the refusal holds.
    public static TheoryData<string[], string?, string> Refusals => new()
    {
        { ["en", """{ "menu": { "title": "Title" } }""", "de", """{ "menu": { "title": "Titel", "begin": "Los" } }"""], null,
            "Assets/de.strings.json: menu.begin: the source language has no such key. Rename it to match or remove it." },
        { ["en", """{ "menu": { "title": "Title" } }""", "de", """{ "menu": "Menü" }"""], null,
            "Assets/de.strings.json: menu: is a text here and a group in Assets/en.strings.json. Make it a group" },
        { ["en", """{ "score": "Score: {points}" }""", "de", """{ "score": "Punkte: {punkte}" }"""], null,
            "Assets/de.strings.json: score: names {punkte}, and the source in Assets/en.strings.json names {points}." },
        { ["en", """{ "title": "A" }""", "hud/en", """{ "title": "B" }"""], null,
            "Assets/hud/en.strings.json: title: is already defined in Assets/en.strings.json. Keep it in one file." },
        { ["en", """{ "title": "A" }""", "pt-br", """{ "title": "B" }"""], null,
            "Assets/pt-br.strings.json: is named for \"pt-br\", which BCP 47 spells \"pt-BR\". Rename it pt-BR.strings.json." },
        { ["en", """{ "title": "A" }""", "nope", """{ "title": "B" }"""], null,
            "Assets/nope.strings.json: is named for \"nope\", which is no language tag." },
        { ["fr", """{ "title": "A" }""", "de", """{ "title": "B" }"""], null,
            "Assets: has no string table in the source language \"en\". Set WithSourceLanguage to one of: de, fr." },
        { ["en", """{ "title": "A" }"""], "de",
            "Assets: has no string table in the fallback language \"de\". Add de.strings.json, or set WithFallbackLanguage to one of: en." },
        { ["en", """{ "menu.title": "A" }"""], null, "Assets/en.strings.json: menu.title: has a key \"menu.title\", which holds a '.'" },
        { ["en", """{ "n": "{n, plural, one {#} other {#}}" }""", "pl", """{ "n": "{n, plural, one {#} other {#}}" }"""], null,
            "Assets/pl.strings.json: n: the plural on {n} lacks few, many, which pl selects for a count from 0 to 999. Add a branch for each." },
        { ["en", """{ "n": "{n, plural, one {#} other {#}}" }""", "de", """{ "n": "{count, plural, one {#} other {#}}" }"""], null,
            "Assets/de.strings.json: n: names {count}, and the source in Assets/en.strings.json names {n}." },
        { ["en", """{ "n": "{n, plural, one {{k, plural, other {#}}} other {#}}" }"""], null, "n: has \"{k, plural\" inside a plural branch. A plural or select form nested in a branch is not supported." },
        { ["en", """{ "n": "{n, select, one {#} other {#}}" }"""], null, "n: has \"{n, select\", a select form, which is not supported." },
        { ["en", """{ "n": "{n, plural, single {#} other {#}}" }"""], null, "n: has a plural on {n} with the selector \"single\", which is no plural category." },
        { ["en", """{ "n": "{n, plural, one {#}}" }"""], null, "n: has a plural on {n} with no other branch. Add other {...}, which every plural needs." },
        { ["en", """{ "brace": "a } b" }"""], null, "brace: has a '}' at character 3 that closes no placeholder. Write '}' for a literal brace." },
        { ["en", """{ "brace": "a {{b" }"""], null, "brace: has \"{{\" at character 3. Braces always open or close a placeholder or plural. Write '{' for a literal brace." },
        { ["en", """{ "speech": ["A", "B"] }""", "de", """{ "speech": ["A", "B", "C"] }"""], null,
            "Assets/de.strings.json: speech.2: the source's list in Assets/en.strings.json has 2 texts. Remove this one, or add its text to the source." },
        { ["en", """{ "speech": ["A", "B"] }""", "de", """{ "speech": { "0": "A", "1": "B" } }"""], null,
            "Assets/de.strings.json: speech: is a group here and a list in Assets/en.strings.json. Make it a list" },
        { ["en", """{ "speech": ["A", ["B"]] }"""], null, "Assets/en.strings.json: speech.1: is an array. A list holds texts in quotes alone." },
        { ["en", """{ "speech": [{ "a": "A" }] }"""], null, "Assets/en.strings.json: speech.0: is an object. A list holds texts in quotes alone." },
        { ["en", """{ "speech": [] }"""], null, "Assets/en.strings.json: speech: is an empty list." },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AStringTableTheBuildCannotShip_FailsTheBuildNamingTheFileTheKeyAndTheFix(string[] files, string? fallback, string defect)
    {
        using ToolWorkspace workspace = Authored(files);
        if (fallback is not null)
        {
            workspace.Configure = build => build.WithFallbackLanguage(fallback);
        }

        Assert.Contains(defect, workspace.Fail(), StringComparison.Ordinal);
    }

    // A development build warns and ships the source text in the slot, marked to take the source language's
    // plural rules. French would show 0 as one. A shipping build refuses to ship the slot.
    [Fact]
    public void AKeyATranslationLacks_ShowsTheSourceTextInDevelopment_AndFailsShipping()
    {
        using ToolWorkspace workspace = Authored(
            "en", """{ "a": "A", "b": "B", "c": "{n, plural, one {# item} other {# items}}" }""",
            "de", """{ "a": "Ä" }""",
            "ar", """{ "a": "ا", "b": "ب" }""",
            "fr", """{ "a": "a", "b": "b" }""");
        const string Missing = "Assets/de.strings.json: b: this language lacks the key Assets/en.strings.json defines. Add it.";

        workspace.Succeed();

        Assert.Contains(Missing, workspace.Output, StringComparison.Ordinal);
        Type strings = Loaded(workspace).GetType("Capsule.Generated.CapsuleStrings")!;
        StringTable table = (StringTable)strings.GetProperty("Table")!.GetValue(null)!;
        Assert.Equal(["en", "ar", "de", "fr"], table.Languages.ToArray());
        Assert.Equal(["Ä", "\u0008B"], Texts(table, 2)[..2]);

        Label items = new(FontFixtures.Font());
        Scene scene = new();
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, items));
        using SceneSimulation simulation = new(scene, run: new Run { Language = "fr" });
        items.SetText((TextKey)strings.GetProperty("C")!.GetValue(null)!, 0);
        Assert.Equal("0 items", items.Text);

        // Each language's texts are read from the assembly's data.
        Assert.Equal(0, StepMeasurement.Bytes(_ => table.Text(2, out ReadOnlySpan<byte> _), warmup: 10, measured: 100));

        Assert.Contains(Missing + " A shipping build ships no text left untranslated.", workspace.Fail("--shipping"), StringComparison.Ordinal);
    }

    // A list's texts are keys like any other, consecutive between its neighbours. A translation short of the
    // source's count lacks a key and shows the source text there.
    [Fact]
    public void AList_DeclaresATextListOverItsTexts_AndATranslationShortOfItsCountLacksAKey()
    {
        using ToolWorkspace workspace = Authored(
            "en", """{ "intro": "Hi", "dialogue": { "speech": ["One {n}", "Two", "Three"] }, "outro": "Bye" }""",
            "de", """{ "intro": "Hallo", "dialogue": { "speech": ["Eins {n}", "Zwei"] }, "outro": "Tschau" }""");

        workspace.Succeed();

        Assert.Contains("Assets/de.strings.json: dialogue.speech.2: this language lacks the key Assets/en.strings.json defines.", workspace.Output, StringComparison.Ordinal);
        Type dialogue = Loaded(workspace).GetType("Capsule.Generated.CapsuleStrings")!.GetNestedType("Dialogue")!;
        TextList speech = (TextList)dialogue.GetProperty("Speech")!.GetValue(null)!;
        Assert.Equal(["dialogue.speech.0", "dialogue.speech.1", "dialogue.speech.2"], Enumerable.Range(0, speech.Count).Select(i => speech[i].ToString()));
        Assert.Equal(1, speech[0].ArgumentCount);
        Assert.Equal(["Hallo", "Eins \u00010", "Zwei", "\u0008Three", "Tschau"], Texts(speech[0].Table, 1));
    }

    // A placeholder's index is its order in the source text. A translation may move it, and it keeps its
    // name. An apostrophe quotes a brace anywhere and # only in a plural branch, and is text otherwise.
    [Fact]
    public void ATranslationsReorderedPlaceholders_BindByName()
    {
        using ToolWorkspace workspace = Authored(
            "en", """{ "hud": { "pair": "{first} of {second}, '{'literal'}' don't '#' {first, plural, one {x} other {'#'#}}" } }""",
            "de", """{ "hud": { "pair": "{second} hat {first}" } }""");

        workspace.Succeed();

        Type hud = Loaded(workspace).GetType("Capsule.Generated.CapsuleStrings")!.GetNestedType("Hud")!;
        TextKey pair = (TextKey)hud.GetProperty("Pair")!.GetValue(null)!;
        Assert.Equal("hud.pair", pair.ToString());
        Assert.Equal(2, pair.ArgumentCount);
        Assert.Equal(["\u00010 of \u00011, {literal} don't '#' \u00020\u00031x\u00035#\u0006\u0007"], Texts(pair.Table, 0));
        Assert.Equal(["\u00011 hat \u00010"], Texts(pair.Table, 1));
    }

    // A scene document's key path finds its text by the UTF-8 order the build sorts the paths in, which here
    // differs from the key order. A path that only prefixes another, or names a group, finds none.
    [Fact]
    public void EveryKeyPath_FindsItsText()
    {
        using ToolWorkspace workspace = Authored("en", """{ "z": "Z", "a": { "bc": "ABC", "b": "AB" }, "a_c": "AC", "b": "B" }""");

        workspace.Succeed();

        StringTable table = (StringTable)Loaded(workspace).GetType("Capsule.Generated.CapsuleStrings")!.GetProperty("Table")!.GetValue(null)!;
        string[] paths = [.. Enumerable.Range(0, table.KeyCount).Select(table.KeyPath)];
        Assert.Equal(["z", "a.bc", "a.b", "a_c", "b"], paths);
        Assert.Equal(Enumerable.Range(0, paths.Length), paths.Select(path => table.Find(path)!.Value.Index));
        Assert.All(["a", "a.bcd", "é", "y", ""], path => Assert.Null(table.Find(path)));
    }

    // Every integer sample the CLDR lists selects its category in the build's rule model.
    [Fact]
    public void ThePluralRuleModel_SelectsEachCldrIntegerSamplesCategory()
    {
        List<string> wrong = [];
        foreach (PluralRules rules in PluralRules.All.Values)
        {
            foreach (PluralCategory category in Enum.GetValues<PluralCategory>())
            {
                wrong.AddRange(rules.Samples(category).Where(n => rules.Select(n) != category).Select(n => $"{rules.Locale} {n}: {rules.Select(n)}, not {category}"));
            }
        }

        Assert.Empty(wrong);
        Assert.Contains(PluralRules.All.Values, static rules => rules.Samples(PluralCategory.Few).Any());
    }

    // The generated code selects each sample's category in languages of distinct rule shapes. French may
    // leave out many, which only a count from a million selects, and shows other there.
    [Fact]
    public void TheCompiledPluralRules_SelectEachCldrIntegerSamplesCategory()
    {
        string[] languages = ["en", "ar", "cy", "fr", "pl", "ru"];
        using ToolWorkspace workspace = new();
        foreach (string language in languages)
        {
            PluralCategory[] forms = PluralRules.For(language)!.Required;
            string branches = string.Join(" ", forms.Select(static form => PluralRules.Names[(int)form] + " {#}"));
            workspace.Write($"Assets/{language}.strings.json", $$$"""{ "n": "{n, plural, {{{branches}}}}" }""");
        }

        workspace.Succeed();

        StringTable table = (StringTable)Loaded(workspace).GetType("Capsule.Generated.CapsuleStrings")!.GetProperty("Table")!.GetValue(null)!;
        Assert.Equal(languages, table.Languages.ToArray());
        Assert.DoesNotContain(PluralCategory.Many, PluralRules.For("fr")!.Required);
        List<string> wrong = [];
        for (int language = 0; language < languages.Length; language++)
        {
            PluralRules rules = PluralRules.For(languages[language])!;
            foreach (PluralCategory category in Enum.GetValues<PluralCategory>())
            {
                wrong.AddRange(rules.Samples(category).Where(n => table.Category(language, n) != category).Select(n => $"{languages[language]} {n}: {table.Category(language, n)}, not {category}"));
            }
        }

        Assert.Empty(wrong);
    }

    private static string[] Texts(StringTable table, int language) =>
        [.. Enumerable.Range(0, table.KeyCount).Select(key => Encoding.UTF8.GetString(table.Template(language, key)))];

    // Each pair is a file's path under Assets/ without .strings.json, then its JSON.
    private static ToolWorkspace Authored(params string[] files)
    {
        ToolWorkspace workspace = new();
        for (int i = 0; i < files.Length; i += 2)
        {
            workspace.Write($"Assets/{files[i]}.strings.json", files[i + 1]);
        }

        return workspace;
    }

    // The generated table as the game runs it, beside the attribute CapsuleAssets declares for its members.
    private static Assembly Loaded(ToolWorkspace workspace) =>
        GeneratorHarness.Loaded(CSharpCompilation.Create(
            "StringSpecs",
            [
                CSharpSyntaxTree.ParseText(workspace.Generated),
                CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ToolWorkspace.Out, "CapsuleStrings.g.cs"))),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
}
