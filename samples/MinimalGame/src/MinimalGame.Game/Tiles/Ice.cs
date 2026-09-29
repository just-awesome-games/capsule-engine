using Capsule.Scenes;
using Capsule.Tiles;

namespace MinimalGame.Game.Tiles;

/// <summary>Ground the player slides on, speeding up and slowing down over several steps.</summary>
public sealed class Ice : TileType
{
    /// <summary>
    /// How fast the player's speed on ice changes toward the walk speed the input asks for, in px/s per second,
    /// 160 by default.
    /// </summary>
    /// <remarks>
    /// The default stops a full-speed walk in half a second. Higher grips sooner, and lower slides longer.
    /// </remarks>
    [Authorable]
    public float Friction { get; private set; } = 160f;
}
