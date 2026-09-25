using Capsule.Rendering;

namespace Capsule.Input;

/// <summary>
/// The run's mouse cursor: whether it shows, what it looks like and whether it is held inside the
/// window. Reached as <c>Run.Cursor</c> and held for the run.
/// </summary>
/// <remarks>
/// The host applies the cursor each frame, after that frame's steps, and it moves at display rate. Nothing about it feeds
/// back into the simulation. State set once holds across scene transitions. The development overlay
/// shows the system arrow, unconfined, while it is open. A headless run shows nothing.
/// </remarks>
public sealed class Cursor
{
    internal Cursor()
    {
    }

    /// <summary>Whether the cursor shows over the window, true by default.</summary>
    /// <remarks>
    /// The host hides the cursor while the gamepad is <see cref="InputState.ActiveDevice"/>, whatever
    /// this says. It shows again when the mouse is used.
    /// </remarks>
    public bool Visible { get; set; } = true;

    /// <summary>The sprite drawn as the cursor, or null for the system arrow.</summary>
    /// <remarks>
    /// The sprite's pivot is the hotspot, the texel that points. Any sprite cut from the game's
    /// textures works, including an atlas-packed texture or a frame of a sheet. The host scales the
    /// image with the canvas by the nearest whole factor, at least 1, with nearest-neighbour sampling.
    /// The image then keeps its size against the game's art. The factor is lowered, down to 1, to keep
    /// each side within 256 window pixels, and a region already wider than that stays at 1.
    /// </remarks>
    /// <example>
    /// An animated cursor is a clip the game steps and assigns every step:
    /// <code>
    /// _busy.Step(Busy.FrameTicks, Busy.Loop);
    /// Run.Cursor.Image = Busy.Frames[_busy.FrameIndex];
    /// </code>
    /// </example>
    public Sprite? Image
    {
        get;

        set
        {
            if (value is { } sprite)
            {
                RequireHotspot(sprite, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Whether the cursor is held inside the window while the window has focus.</summary>
    public bool Confined { get; set; }

    private static void RequireHotspot(in Sprite sprite, string parameterName)
    {
        float x = MathF.Floor(sprite.Pivot.X);
        float y = MathF.Floor(sprite.Pivot.Y);
        if (x >= 0f && x < sprite.Region.Width && y >= 0f && y < sprite.Region.Height)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            sprite.Pivot,
            $"The cursor's pivot is its hotspot and lies outside its {sprite.Region.Width} by {sprite.Region.Height} region. Move the pivot onto the texel that points.");
    }
}
