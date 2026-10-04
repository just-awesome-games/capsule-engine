using Capsule.Generated;

namespace MinimalGame.Tests;

public sealed class SceneDocumentTests
{
    // Every document the game ships composes as a run would load it, so a bad placement fails here
    // and not when a player walks into the room.
    [Fact]
    public void EveryScenePlacementIsValid() => CapsuleScenes.Registry.ComposeAll();
}
