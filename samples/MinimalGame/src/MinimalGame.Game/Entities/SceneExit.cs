using Capsule.Scenes;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>Leaves for another room, arriving at the entrance there that its arrival key names.</summary>
/// <remarks>The entity decides what triggers the exit and calls <see cref="Leave"/>.</remarks>
public sealed class SceneExit : Component
{
    private bool _leaving;

    /// <summary>The room to leave for.</summary>
    [Authorable(Required = true)]
    public SceneKey Destination { get; set; }

    /// <summary>The key of the <see cref="Entrance"/> in <see cref="Destination"/> the player arrives at.</summary>
    [Authorable(Required = true)]
    public string ArriveAt { get; set; } = string.Empty;

    /// <summary>Requests the destination room once, however often a trigger fires.</summary>
    public void Leave()
    {
        if (!_leaving)
        {
            _leaving = true;
            Run.RequestScene(Destination, new Arrival(ArriveAt));
        }
    }
}
