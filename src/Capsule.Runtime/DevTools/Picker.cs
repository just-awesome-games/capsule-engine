using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// What a held frame draws under a world point, read back through the entity each run of its world sprites
// was attributed to. A sprite is hit anywhere inside its drawn rect, whatever its texels' alpha.
internal static class Picker
{
    // Fills stack with the entities drawn at point, topmost first. Each comes after those of its ancestors
    // not yet listed, root first. A click takes the whole before its parts. parallax is the frame's
    // ScrollLayout.Parallax, which moves point into each scrolled layer.
    internal static void Pick(FrameView frame, ReadOnlySpan<DrawRun> runs, Vector2 point, Vector2 parallax, List<Entity> stack)
    {
        stack.Clear();
        ReadOnlySpan<SpriteIntent> sprites = frame.Sprites;
        ReadOnlySpan<ParallaxLayer> layers = frame.ParallaxLayers;
        int layer = layers.Length - 1;
        int run = runs.Length - 1;
        for (int index = sprites.Length - 1; index >= 0; index--)
        {
            while (run >= 0 && runs[run].FirstSprite > index)
            {
                run--;
            }

            while (layer >= 0 && layers[layer].FirstSprite > index)
            {
                layer--;
            }

            if (run < 0)
            {
                return;
            }

            Vector2 factor = layer >= 0 ? layers[layer].ScrollFactor : Vector2.One;
            if (Covers(sprites[index], ScrollLayout.InLayer(point, parallax, factor)))
            {
                Add(stack, runs[run].Entity);

                // The rest of the run is the same entity's.
                index = runs[run].FirstSprite;
            }
        }
    }

    // The drawn rect, turned about the pivot by the sprite's settled rotation, holds point.
    private static bool Covers(in SpriteIntent sprite, Vector2 point)
    {
        TextureRegion region = sprite.Sprite.Region;
        Vector2 size = sprite.Size;
        if (!(size.X > 0f) || !(size.Y > 0f) || region.Width <= 0 || region.Height <= 0 || !float.IsFinite(sprite.Rotation))
        {
            return false;
        }

        Vector2 local = point - sprite.Position;
        if (sprite.Rotation != 0f)
        {
            (float sin, float cos) = MathF.SinCos(sprite.Rotation);
            local = new Vector2((local.X * cos) + (local.Y * sin), (local.Y * cos) - (local.X * sin));
        }

        local += sprite.DrawOrigin * new Vector2(size.X / region.Width, size.Y / region.Height);

        return local.X >= 0f && local.Y >= 0f && local.X < size.X && local.Y < size.Y;
    }

    private static void Add(List<Entity> stack, Entity entity)
    {
        int at = stack.Count;
        for (Entity? above = entity; above is not null && !stack.Contains(above); above = above.Parent)
        {
            stack.Insert(at, above);
        }
    }
}
