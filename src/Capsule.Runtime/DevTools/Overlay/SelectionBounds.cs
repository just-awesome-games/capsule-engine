using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Overlay;

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
                    Include(ref union, ref any, bounds);
                }
                else if (component is Collider2D collider)
                {
                    Aabb2D box = collider.Bounds;
                    Include(ref union, ref any, Rect.FromCorners(box.Min, box.Max));
                }
            }
        }

        foreach (Entity child in entity.Children)
        {
            Grow(child, ref union, ref any);
        }
    }

    private static void Include(ref Rect union, ref bool any, in Rect added)
    {
        if (!float.IsFinite(added.Left + added.Top + added.Right + added.Bottom))
        {
            return;
        }

        union = any ? Rect.Union(union, added) : added;
        any = true;
    }
}
