using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Rendering;

/// <summary>Adds one convex polygon of flat colour into the frame's light, which multiplies every lit world sprite drawn inside it.</summary>
/// <remarks>
/// The points are in the entity's own units, placed by its world transform: its position, turn and scale. Between
/// steps each point moves in a straight line from where the entity's last step left it, and a new outline shows
/// from the step that sets it, as a <see cref="ColorPolygon"/>'s does. The light has no falloff: every pixel inside
/// the outline takes the whole colour. Two lights add, and <see cref="Renderer.ZIndex"/> has no effect on one.
/// Drawing under a <see cref="ScreenEntity"/> throws, because the screen layer is never lit. A light draws nothing
/// itself, and a visible beam is a <see cref="ColorPolygon"/> on the same points.
/// </remarks>
/// <example>
/// <code>
/// ReadOnlySpan&lt;Vector2&gt; cone = [Vector2.Zero, new Vector2(-40f, 120f), new Vector2(40f, 120f)];
/// PolygonLight beam = Add(new PolygonLight(cone) { Color = ColorRgba.Yellow });
/// </code>
/// </example>
public sealed class PolygonLight : Renderer
{
    private ConvexOutline _outline;

    /// <summary>The light's colour, added into the light scaled by its alpha. White by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>How many times the colour is added, 1 by default. Zero adds nothing.</summary>
    /// <remarks>A light of one on a white ambient doubles the authored colour inside it, and above one brightens further, with no ceiling.</remarks>
    public float Intensity
    {
        get;
        set => field = Guard.NonNegative(value, nameof(value));
    } = 1f;

    /// <summary>A light with no points, which adds nothing until <see cref="SetPoints"/> gives it an outline.</summary>
    public PolygonLight()
    {
    }

    /// <summary>A light with the outline <paramref name="points"/> describes. See <see cref="SetPoints"/>.</summary>
    /// <param name="points">The outline, in the entity's own units.</param>
    public PolygonLight(ReadOnlySpan<Vector2> points) => SetPoints(points);

    /// <summary>The outline's points in order, in the entity's own units. Empty until an outline is set.</summary>
    public ReadOnlySpan<Vector2> Points => _outline.Points;

    internal override bool Steps => false;

    /// <summary>
    /// The rect the outline covers, under the rules <see cref="Renderer.Bounds"/> states. Reads empty while the
    /// light has no outline or adds nothing.
    /// </summary>
    public override Rect Bounds => Entity is null || !(Intensity > 0f) ? default : _outline.Cover(RenderTransform);

    /// <summary>Replaces the outline with a copy of <paramref name="points"/>, in the entity's own units.</summary>
    /// <remarks>
    /// The points run in order around a convex outline, either way round, three of them or more, as
    /// <see cref="ColorPolygon.SetPoints"/> takes them. An outline no longer than one the light has held allocates
    /// nothing.
    /// </remarks>
    /// <param name="points">The outline's points, in order.</param>
    public void SetPoints(ReadOnlySpan<Vector2> points)
    {
        _outline.Set(points);
    }

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (_outline.Count == 0)
        {
            return;
        }

        _outline.Place(PreviousRenderTransform, RenderTransform, out ReadOnlySpan<Vector2> before, out ReadOnlySpan<Vector2> now);

        view.AddLight(new PolygonIntent(before, now, Color), Intensity);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        panel.Field("Points", _outline.Count);
        panel.Field("Color", Color);
        panel.Field("Intensity", Intensity);
    }
}
