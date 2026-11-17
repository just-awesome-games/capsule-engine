using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Rendering;

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
            Assert.Equal(TextureSampling.Point, Read(spelled));
        }
        else
        {
            Assert.Throws<JsonException>(() => Read(spelled));
        }
    }

    private static TextureSampling Read(string spelled)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes($"\"{spelled}\""));
        reader.Read();
        return new LowerCaseEnumConverter<TextureSampling>().Read(ref reader, typeof(TextureSampling), JsonSerializerOptions.Default);
    }
}
