namespace MinimalGame.Game.Scenes.Halls;

/// <summary>The halls, built from the room's tiles in warmer colours.</summary>
public sealed class Hall : PlayableScene
{
    public Hall(SceneContent content)
        : base(content)
    {
        ShaderGlobals.Set("Shadow", new ColorRgba(59, 31, 43));
        ShaderGlobals.Set("Light", new ColorRgba(217, 160, 102));
        Material duotone = new(CapsuleAssets.Shaders.DuotoneShader);

        // The shader loads with the scene rather than on its first draw, because the constructor sets it.
        FindSingle<TileMap>().Material = duotone;
    }
}
