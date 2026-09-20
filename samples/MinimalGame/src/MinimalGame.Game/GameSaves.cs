using System.Text.Json.Serialization;
using Capsule.Input;
using Capsule.Persistence;

namespace MinimalGame.Game;

/// <summary>What the player keeps between runs. One document, written when the options screen changes it.</summary>
public sealed record GameSettings
{
    /// <summary>Whether sound effects are audible; the <c>sfx</c> bus is levelled from it at boot.</summary>
    public bool SoundOn { get; set; } = true;

    /// <summary>The bindings the player may change.</summary>
    public InputSettings Input { get; set; } = new();
}

/// <summary>One pair per rebindable action, so its two devices are edited and saved as one thing.</summary>
public sealed record InputSettings
{
    /// <summary>What leaves the floor.</summary>
    public Binding Jump { get; set; } = new(Key.Space, PadButton.South);

    /// <summary>What fires a bolt.</summary>
    public Binding Shoot { get; set; } = new(MouseButton.Left, PadButton.West);
}

/// <summary>What one action is bound to on each device.</summary>
public readonly record struct Binding(InputButton Key, InputButton Pad)
{
    /// <summary>This pair with the slot for <paramref name="button"/>'s device replaced.</summary>
    public Binding With(InputButton button) =>
        button.Device == InputDevice.Gamepad ? this with { Pad = button } : this with { Key = button };

    /// <summary>The slot a prompt shows for <paramref name="device"/>.</summary>
    public InputButton For(InputDevice device) => device == InputDevice.Gamepad ? Pad : Key;
}

/// <summary>The game's save documents, declared once beside the rest of its declarations.</summary>
public static class GameSaves
{
    /// <summary>The settings document. A first run has none, so it reads as the default settings.</summary>
    public static readonly SaveKey<GameSettings> Settings =
        new("settings", GameSaveContext.Default.GameSettings, new GameSettings());
}

// Serialization is generated, because a game ships under NativeAOT and reflection-based
// serialization is off.
[JsonSerializable(typeof(GameSettings))]
internal sealed partial class GameSaveContext : JsonSerializerContext;
