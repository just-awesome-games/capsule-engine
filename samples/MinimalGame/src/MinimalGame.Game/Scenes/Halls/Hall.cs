using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;

namespace MinimalGame.Game.Scenes.Halls;

/// <summary>The halls, built from the room's tiles in warmer colours.</summary>
public sealed class Hall : PlayableScene
{
    public Hall(SceneContent content)
        : base(content)
    {
        Material brick = new(CapsuleAssets.Shaders.DuotoneShader);
        brick.Set("Shadow", new ColorRgba(59, 31, 43));
        brick.Set("Light", new ColorRgba(217, 160, 102));

        // Set in the constructor, so the shader loads with the scene rather than on its first draw.
        FindSingle<TileMap>().Material = brick;
    }
}
