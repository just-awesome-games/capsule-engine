using Capsule.Input;

namespace PackageConsumer.Game;

/// <summary>The actions this consumer binds, as a game's own input module does.</summary>
public static class ConsumerInput
{
    public static readonly InputAction Advance = new("advance");

    public static readonly InputAction Quit = new("quit");

    public static void Bind(ActionBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        bindings.Bind(Advance, Key.D, PadButton.DPadRight);
        bindings.Bind(Quit, Key.Escape, PadButton.Start);
    }
}
