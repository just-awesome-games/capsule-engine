using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

public sealed class EntryMembersFormatTests
{
    // A game entry's members are its class's to read, so the writer keeps their values as authored.
    [Fact]
    public void AGameEntrysMembers_RoundTripAsAuthored()
    {
        const string Json = """{"entities":[{"id":1,"type":"lift","x":8,"rise":96.5,"tiles":[1,2],"shape":["["],"path":[[0,0],[48,-1.5]],"label":null}]}""";

        Assert.Equal(Json, SceneDocument.Parse(Json).ToJson());
    }
}
