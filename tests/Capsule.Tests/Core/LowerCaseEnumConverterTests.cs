using System.Text;
using System.Text.Json;
using Capsule.Assets;

namespace Capsule.Tests.Core;

public sealed class LowerCaseEnumConverterTests
{
    // "1" names a defined member by number and "5" names none. Both parse as numbers.
    [Theory]
    [InlineData("point", true)]
    [InlineData("Point", false)]
    [InlineData("1", false)]
    [InlineData("5", false)]
    public void Read_TakesOnlyAMembersLowerCaseName(string spelled, bool taken)
    {
        if (taken)
        {
            Assert.Equal(TextureSamplingSetting.Point, Read(spelled));
        }
        else
        {
            Assert.Throws<JsonException>(() => Read(spelled));
        }
    }

    private static TextureSamplingSetting Read(string spelled)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes($"\"{spelled}\""));
        reader.Read();
        return new LowerCaseEnumConverter<TextureSamplingSetting>().Read(ref reader, typeof(TextureSamplingSetting), JsonSerializerOptions.Default);
    }
}
