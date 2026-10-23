using Capsule.Localization;

namespace Capsule.Tests.Localization;

public sealed class LanguageMatcherTests
{
    // Each preferred tag tries an exact match, then a shorter tag, then another region of its language,
    // before the next preferred tag is tried. Chinese never crosses scripts.
    [Theory]
    [InlineData("en,de,pt-BR", "DE", "de")]
    [InlineData("en,de,pt-BR", "de_AT", "de")]
    [InlineData("en,de,pt-BR", "pt-PT", "pt-BR")]
    [InlineData("en,fr-FR", "fr-CA,en", "fr-FR")]
    [InlineData("en,fr-FR,fr-CA", "fr-CA", "fr-CA")]
    [InlineData("en,zh-Hans,zh-Hant", "zh-TW", "zh-Hant")]
    [InlineData("en,zh-Hans,zh-Hant", "zh-HK", "zh-Hant")]
    [InlineData("en,zh-Hans,zh-Hant", "zh-CN", "zh-Hans")]
    [InlineData("en,zh-Hans,zh-Hant", "zh", "zh-Hans")]
    [InlineData("en,zh-Hant", "zh-CN,en", "en")]
    [InlineData("en,zh", "zh-TW", null)]
    [InlineData("en,de", "ja,ko", null)]
    public void ThePreferredTags_PickTheClosestShippedLanguage(string shipped, string preferred, string? expected)
    {
        string[] languages = shipped.Split(',');

        int matched = LanguageMatcher.Match(languages, preferred.Split(','));

        Assert.Equal(expected is null ? -1 : Array.IndexOf(languages, expected), matched);
    }
}
