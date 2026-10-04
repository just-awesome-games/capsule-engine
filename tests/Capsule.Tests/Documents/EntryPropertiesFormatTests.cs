using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

public sealed class EntryPropertiesFormatTests
{
    // A game entry's properties are its class's to read, so the writer keeps their values as authored.
    [Fact]
    public void AGameEntrysProperties_RoundTripAsAuthored()
    {
        const string Json = """{"entities":[{"id":1,"type":"lift","x":8,"rise":96.5,"tiles":[1,2],"shape":["["],"path":[[0,0],[48,-1.5]],"label":null}]}""";

        Assert.Equal(Json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(Json)));
    }
}
