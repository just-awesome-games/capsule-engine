using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// The run's window and frame pacing: fullscreen, the window's size, vertical sync and the frame caps.
/// Reached as <c>Run.Display</c> and held for the run.
/// </summary>
/// <remarks>
/// The shell's builder sets where each starts, and game code changes them from a settings screen. A
/// run-start hook runs before the window opens, and the window opens in the settings a game restores
/// there. The host applies a change after the frame's steps. <see cref="RefreshRate"/> is the one value
/// the host writes, before a frame's steps. A headless run ignores the settings and reads a refresh rate
/// of 0.
/// </remarks>
/// <example>
/// A game keeps its player's choices in a save document of its own and applies them at run start:
/// <code>
/// GameSettings settings = run.Saves.Read(GameSaves.Settings);
/// run.Display.Fullscreen = settings.Fullscreen;
/// run.Display.MaxFrameRate = settings.MaxFrameRate;
/// </code>
/// </example>
public sealed class Display
{
    /// <summary>Whether the window covers its display, borderless and at the display's own resolution.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>The window's client size at 100% display scaling while it is not fullscreen, 1280 by 720 by default.</summary>
    /// <remarks>
    /// The host multiplies it by the scale of the display the window is on, rounds it to whole pixels
    /// and shrinks one larger than the display to fit, keeping its aspect. A window the player resizes
    /// keeps the player's size until the game asks for another.
    /// </remarks>
    public Vector2 WindowSize
    {
        get;

        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X < 1f || value.Y < 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A window size component must be finite and at least one pixel.");
            }

            field = value;
        }
    } = new(1280f, 720f);

    /// <summary>How each frame waits for the display, <see cref="VSyncMode.On"/> by default.</summary>
    /// <remarks>
    /// Under <see cref="VSyncMode.Mailbox"/> or <see cref="VSyncMode.Off"/> the window draws as often as
    /// <see cref="MaxFrameRate"/> allows, and with no cap as often as the machine can. A settings screen
    /// that offers them offers the cap beside them.
    /// </remarks>
    public VSyncMode VSync { get; set; }

    /// <summary>The most frames the window presents a second, or 0 for no cap beyond <see cref="VSync"/>.</summary>
    /// <remarks>
    /// The host sleeps until each frame is due, and the processor idles between frames. With vertical
    /// sync on, the slower of the two paces the window, and a cap that divides the display's refresh
    /// rate paces evenly. A cap never slows the simulation: a frame runs every step its period covers.
    /// </remarks>
    public int MaxFrameRate
    {
        get;
        set => field = NonNegativeRate(value);
    }

    /// <summary>
    /// The most frames the window presents a second while it lacks focus, 30 by default, or 0 to keep
    /// the focused rate.
    /// </summary>
    /// <remarks>
    /// It only lowers the rate: while unfocused, the lower of this and <see cref="MaxFrameRate"/>
    /// applies. A minimized window draws nothing and wakes at most once a step whatever this says.
    /// </remarks>
    public int UnfocusedMaxFrameRate
    {
        get;
        set => field = NonNegativeRate(value);
    } = 30;

    /// <summary>The refresh rate in hertz of the display the window is on, or 0 where the platform reports none.</summary>
    /// <remarks>
    /// It follows the window to another display and a change of the display's mode. Under
    /// <see cref="VSyncMode.On"/> each frame already waits for a refresh. A settings screen offers it as a
    /// <see cref="MaxFrameRate"/> beside <see cref="VSyncMode.Mailbox"/> or <see cref="VSyncMode.Off"/>.
    /// </remarks>
    public double RefreshRate { get; internal set; }

    internal Display()
    {
    }

    // The client size the host gives the window on a display of this extent and scale: the request
    // scaled, in whole pixels, shrunk to fit with its aspect kept.
    internal (int Width, int Height) WindowPixels(int displayWidth, int displayHeight, float displayScale) =>
        Fit(WindowSize * displayScale, displayWidth, displayHeight);

    // The client size that keeps a window of width by height pixels looking the same size after its
    // display's scale moves from one value to another, shrunk to fit the new display with its aspect kept.
    internal static (int Width, int Height) Rescaled(int width, int height, float fromScale, float toScale, int displayWidth, int displayHeight) =>
        Fit(new Vector2(width, height) * (toScale / fromScale), displayWidth, displayHeight);

    private static (int Width, int Height) Fit(Vector2 scaled, int displayWidth, int displayHeight)
    {
        float fit = MathF.Min(1f, MathF.Min(displayWidth / scaled.X, displayHeight / scaled.Y));

        return (Math.Max(1, (int)MathF.Round(scaled.X * fit)), Math.Max(1, (int)MathF.Round(scaled.Y * fit)));
    }

    private static int NonNegativeRate(int value) =>
        value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A frame rate cap must be zero, for none, or a positive number of frames a second.");
}
