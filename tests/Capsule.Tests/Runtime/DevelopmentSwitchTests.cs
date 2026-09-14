using System.Xml.Linq;
using Capsule.Diagnostics;

namespace Capsule.Tests.Runtime;

public sealed class DevelopmentSwitchTests
{
    [Fact]
    public void Development_IsOnWhenTheTestProcessHasNoSwitch()
    {
        Assert.True(Development.IsSupported);
    }

    [Fact]
    public void ShippingTargetDisablesTheOneDevelopmentSwitch()
    {
        XDocument targets = XDocument.Load(DevelopmentTargetsPath());
        XElement option = Assert.Single(
            targets.Descendants("RuntimeHostConfigurationOption"),
            static element => (string?)element.Attribute("Include") == "Capsule.Development");

        Assert.Equal("false", (string?)option.Attribute("Value"));
        Assert.Equal("true", (string?)option.Attribute("Trim"));
        Assert.Equal("'$(CapsuleShipping)' == 'true'", (string?)option.Parent?.Attribute("Condition"));
    }

    private static string DevelopmentTargetsPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "build", "Capsule.DevelopmentOnly.targets");
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The repository's development targets were not found.");
    }
}
