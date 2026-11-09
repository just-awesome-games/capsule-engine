using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Overlay;

// What a held frame draws under a world point, read back through the entity each run of its world sprites
// and polygons was attributed to. A sprite is hit anywhere inside its drawn rect, whatever its texels' alpha,
// and a polygon anywhere inside its outline.
internal static class Picker
{
    // Fills stack with the entities drawn at point, topmost first. Each comes after those of its ancestors
    // not yet listed, root first. A click takes the whole before its parts. parallax is the frame's
    // ScrollLayout.Parallax, which moves point into each scrolled layer. A screen root sorted with the world
    // is in canvas pixels and is never picked, as the screen layer is not.
    internal static void Pick(FrameView frame, ReadOnlySpan<DrawRun> runs, Vector2 point, Vector2 parallax, List<Entity> stack)
    {
        stack.Clear();
        ReadOnlySpan<SpriteIntent> sprites = frame.Sprites;
        ReadOnlySpan<StoredPolygon> polygons = frame.Polygons;
        ReadOnlySpan<PlacementRun> layers = frame.PlacementRuns;
        int layer = layers.Length - 1;
        int run = runs.Length - 1;
        int sprite = sprites.Length - 1;
        int polygon = polygons.Length - 1;
        while (sprite >= 0 || polygon >= 0)
        {
            // The topmost intent left. A polygon draws over the sprites stored before it.
            bool isPolygon = polygon >= 0 && (sprite < 0 || polygons[polygon].Before > sprite);

            // How many sprites and polygons were stored before it, which every run opened before it has reached.
            int spritesBefore = isPolygon ? polygons[polygon].Before : sprite;
            int polygonsBefore = isPolygon ? polygon : polygon + 1;
            while (run >= 0 && (runs[run].FirstSprite > spritesBefore || runs[run].FirstPolygon > polygonsBefore))
            {
                run--;
            }

            while (layer >= 0 && (layers[layer].FirstSprite > spritesBefore || layers[layer].FirstPolygon > polygonsBefore))
            {
                layer--;
            }

            if (run < 0)
            {
                return;
            }

            Vector2 factor = layer >= 0 ? layers[layer].ScrollFactor : Vector2.One;
            Vector2 within = ScrollLayout.InLayer(point, parallax, factor);
            bool canvas = layer >= 0 && layers[layer].Canvas;
            if (!canvas && (isPolygon ? Covers(polygons[polygon], frame.PolygonPoints, within) : Covers(sprites[sprite], within)))
            {
                Add(stack, runs[run].Entity);

                // The rest of the run is the same entity's.
                sprite = runs[run].FirstSprite - 1;
                polygon = runs[run].FirstPolygon - 1;
                continue;
            }

            if (isPolygon)
            {
                polygon--;
            }
            else
            {
                sprite--;
            }
        }
    }

    // The outline's fan from its first point, where the step left it, holds point. A triangle with no area draws
    // nothing and holds nothing.
    private static bool Covers(in StoredPolygon polygon, ReadOnlySpan<Vector2> points, Vector2 point)
    {
        ReadOnlySpan<Vector2> outline = points.Slice(polygon.FirstPoint + polygon.Count, polygon.Count);
        for (int corner = 1; corner < outline.Length - 1; corner++)
        {
            if (Cross(outline[corner] - outline[0], outline[corner + 1] - outline[0]) == 0f)
            {
                continue;
            }

            float ab = Cross(outline[corner] - outline[0], point - outline[0]);
            float bc = Cross(outline[corner + 1] - outline[corner], point - outline[corner]);
            float ca = Cross(outline[0] - outline[corner + 1], point - outline[corner + 1]);
            if ((ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f))
            {
                return true;
            }
        }

        return false;
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

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
