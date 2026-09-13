using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

public sealed class DefaultFontRuntimeTests
{
    [Fact]
    public void Runtime_EmbedsTheDefaultFontPage()
    {
        Assert.Contains(
            "Capsule.Runtime.Assets.default-font.png",
            typeof(CapsuleEngine).Assembly.GetManifestResourceNames());
    }
}
