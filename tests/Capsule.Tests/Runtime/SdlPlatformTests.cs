using Capsule.Runtime.Desktop;

namespace Capsule.Tests.Runtime;

// The desktop platform's reads of the SDL3 it ships, made against the real library.
public sealed class SdlPlatformTests
{
    // SDL3 answers with an array of pointers to locales. Read as an array of locales, the tags come
    // back as pointer bytes or the read faults.
    [Fact]
    public void PreferredLocales_AreLanguageTags_ReadFromSdl3()
    {
        IReadOnlyList<string> tags = SdlPlatform.PreferredLocales();

        Assert.All(tags, tag => Assert.Matches("^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})?$", tag));
    }
}
