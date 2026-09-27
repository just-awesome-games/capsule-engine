using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;

namespace MinimalGame.Game.Scenes.Halls;

/// <summary>The halls, built from the room's tiles in warmer colours.</summary>
public sealed class Hall : PlayableScene
{
    private static readonly Material Brick = new(CapsuleAssets.Shaders.DuotoneShader);

    public Hall(SceneContent content)
        : base(content)
    {
        Brick.Set("Shadow", new ColorRgba(59, 31, 43));
        Brick.Set("Light", new ColorRgba(217, 160, 102));

        // Set in the constructor, so the shader loads with the scene rather than on its first draw.
        FindSingle<TileMap>().Material = Brick;
    }
}
