namespace Capsule.Rendering;

/// <summary>How the window's frames wait for the display.</summary>
/// <remarks>
/// Where the GPU lacks the mode asked for, the window presents in the nearest one it has, and
/// <see cref="Display.VSync"/> keeps the request.
/// </remarks>
public enum VSyncMode
{
    /// <summary>Each frame waits for the display's refresh.</summary>
    /// <remarks>It never tears, and every GPU has it.</remarks>
    On,

    /// <summary>The display shows the newest finished frame at each refresh.</summary>
    /// <remarks>
    /// It never tears, and input reaches the screen sooner than under <see cref="On"/>. Where the GPU
    /// lacks it, the window presents as <see cref="On"/>.
    /// </remarks>
    Mailbox,

    /// <summary>Each frame shows as soon as it is drawn.</summary>
    /// <remarks>
    /// It may tear, and has the lowest latency. Where the GPU lacks it, the window presents as
    /// <see cref="Mailbox"/>, else <see cref="On"/>.
    /// </remarks>
    Off,
}
