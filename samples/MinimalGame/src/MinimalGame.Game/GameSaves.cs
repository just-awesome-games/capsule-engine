using System.Text.Json.Serialization;

namespace MinimalGame.Game;

/// <summary>What the player keeps between runs. One document, written when the options screen changes it.</summary>
public sealed record GameSettings
{
    /// <summary>The language the player chose, as a BCP 47 tag, or null to follow the system's.</summary>
    public string? Language { get; set; }

    /// <summary>The music bus's level, from 0 to 1.</summary>
    public float MusicVolume { get; set; } = 1f;

    /// <summary>The sound effects bus's level, from 0 to 1.</summary>
    public float EffectsVolume { get; set; } = 1f;

    /// <summary>The bindings the player may change.</summary>
    public InputSettings Input { get; set; } = new();

    /// <summary>Whether the window covers the display.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>The window's size as a whole multiple of the view, 4 by default.</summary>
    public int WindowScale { get; set; } = 4;

    /// <summary>How each frame waits for the display.</summary>
    /// <remarks>Stored under a new name. A document from when this was a switch reads as the default.</remarks>
    [JsonPropertyName("VSyncMode")]
    public VSyncMode VSync { get; set; }

    /// <summary>The most frames a second, 0 for no cap.</summary>
    public int MaxFrameRate { get; set; }

    /// <summary>Hands the display choices to the run, at boot and whenever the options screen changes one.</summary>
    public void ApplyDisplay(Display display)
    {
        display.Fullscreen = Fullscreen;
        display.WindowSize = World.ViewportSize * WindowScale;
        display.VSync = VSync;
        display.MaxFrameRate = MaxFrameRate;
    }
}

/// <summary>One pair per rebindable action, its two devices edited and saved as one thing.</summary>
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
}

/// <summary>The game's save documents, declared once beside the rest of its declarations.</summary>
public static class GameSaves
{
    /// <summary>The settings document, which reads as the default settings on a first run that has none.</summary>
    public static readonly SaveKey<GameSettings> Settings =
        new("settings", GameSaveContext.Default.GameSettings, new GameSettings());
}

// Serialization is generated, because a game ships under NativeAOT and reflection-based
// serialization is off.
[JsonSerializable(typeof(GameSettings))]
internal sealed partial class GameSaveContext : JsonSerializerContext;
