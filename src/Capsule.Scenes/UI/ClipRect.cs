using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.UI;

/// <summary>
/// Clips its <see cref="ScreenEntity"/> and everything beneath it to the entity's rect, for the pointer as
/// well as the eye.
/// </summary>
/// <remarks>
/// The clip is the entity's whole rect, not its <see cref="ScreenEntity.Padding"/>-inset inside, in canvas
/// pixels. It cuts every renderer in the subtree and the pointer: a <see cref="Focusable"/> outside the clip
/// is never hovered, clicked or scrolled over. The directional focus actions ignore it and still reach a
/// clipped item. Clips nest, and an inner clip shows only what the outer clips also show.
/// <para>
/// A clip cannot turn. Rotation anywhere in its entity's ancestry is refused while the component is
/// present, as it is for a <see cref="Focusable"/>. An entity beneath the clip may turn freely.
/// </para>
/// </remarks>
/// <example>
/// A list frame that shows the rows inside it and nothing past its edge:
/// <code>
/// ScreenEntity frame = new(Anchor.Center, Vector2.Zero) { Size = new Vector2(104f, 52f) };
/// frame.Add(new ClipRect());
/// </code>
/// </example>
public sealed class ClipRect : Component
{
    internal override bool Steps => false;

    internal override TransformSupport Supports => TransformSupport.Scale;

    internal override void OnAttachedTo(Entity entity)
    {
        if (entity is not ScreenEntity)
        {
            throw new InvalidOperationException(
                $"A {nameof(ClipRect)} clips a rect on the screen layer, and a {entity.GetType().Name} has none. Attach it to a {nameof(ScreenEntity)}.");
        }

        entity.StaleClip();
    }

    internal override void OnDetachingFrom(Entity entity) => entity.StaleClip();

    // Whether `point`, in canvas pixels, lies inside every clip above `entity`, as the current transforms
    // place them. True when nothing above clips.
    internal static bool Admits(Entity entity, Vector2 point)
    {
        for (ClipRect? level = entity.NearestClip; level is not null; level = level.Entity!.Parent?.NearestClip)
        {
            if (!OuterRect(level.Entity!, previous: false).Contains(point))
            {
                return false;
            }
        }

        return true;
    }

    // Adds one level to `view` for `clip` and each clip above it, each with its own previous and current
    // rect. The renderer interpolates every level before intersecting them. The intersection of the
    // ends is not the end of the intersection.
    internal static void AddLevels(ClipRect clip, FrameView view)
    {
        for (ClipRect? level = clip; level is not null; level = level.Entity!.Parent?.NearestClip)
        {
            view.AddClipLevel(OuterRect(level.Entity!, previous: true), OuterRect(level.Entity!, previous: false));
        }
    }

    // The rect Focusable.Bounds covers with a zero size, which fills the screen entity.
    internal static Rect OuterRect(Entity holder, bool previous)
    {
        ref readonly Transform2D world = ref previous ? ref holder.PreviousWorld : ref holder.World;
        Vector2 origin = previous ? holder.PreviousSpaceOrigin : holder.SpaceOrigin;

        return new Rect(world.TransformPoint(Vector2.Zero) + origin, ScreenEntity.Fill(holder, Vector2.Zero) * world.Scale);
    }
}
