using Capsule.Scenes;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.Entities;

/// <summary>Leaves for another room, arriving at the entrance there that its arrival key names.</summary>
/// <remarks>
/// It is a component because several unrelated entity kinds attach it unchanged: a door, an edge zone,
/// an enemy's portal attack. A component reaches the run through its entity. The entity decides what
/// triggers the exit and calls <see cref="Leave"/>.
/// </remarks>
/// <param name="destination">The room to leave for.</param>
/// <param name="arriveAt">The key of the <see cref="Entrance"/> in <paramref name="destination"/> the player arrives at.</param>
public sealed class SceneExit(SceneKey destination, string arriveAt) : Component
{
    private bool _leaving;

    /// <summary>Requests the destination room once, however often a trigger fires.</summary>
    public void Leave()
    {
        if (!_leaving)
        {
            _leaving = true;
            Run.RequestScene(destination, new Arrival(arriveAt));
        }
    }
}
