using Capsule.Input;

namespace PackageConsumer.Game;

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
