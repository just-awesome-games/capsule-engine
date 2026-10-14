using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Where a selected entity's subtree stands in the world, as its renderers and colliders report it.
internal static class SelectionBounds
{
    // The rect covering every non-empty renderer bounds and every collider bounds under entity, the entity
    // included. False when the subtree reports none.
    internal static bool TryUnion(Entity entity, out Rect union)
    {
        union = default;
        bool any = false;
        Grow(entity, ref union, ref any);

        return any;
    }

    private static void Grow(Entity entity, ref Rect union, ref bool any)
    {
        if (entity.Space == RenderSpace.World)
        {
            foreach (Component component in entity.Components)
            {
                if (component is Renderer { Bounds: { IsEmpty: false } bounds })
                {
                    Include(ref union, ref any, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                }
                else if (component is Collider2D collider)
                {
                    Aabb2D box = collider.Bounds;
                    Include(ref union, ref any, box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
                }
            }
        }

        foreach (Entity child in entity.Children)
        {
            Grow(child, ref union, ref any);
        }
    }

    private static void Include(ref Rect union, ref bool any, float left, float top, float right, float bottom)
    {
        if (!float.IsFinite(left + top + right + bottom))
        {
            return;
        }

        union = any
            ? new Rect(MathF.Min(union.Left, left), MathF.Min(union.Top, top), MathF.Max(union.Right, right), MathF.Max(union.Bottom, bottom))
            : new Rect(left, top, right, bottom);
        any = true;
    }
}
