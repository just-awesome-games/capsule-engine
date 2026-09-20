using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The options screen's menu: Jump and Shoot rebinding, the sound toggle and Back, over one
/// <see cref="FocusNavigator"/>.
/// </summary>
public sealed class OptionsMenu : ScreenEntity
{
    // Canvas pixels between neighbouring items' centres.
    private const float ItemSpacing = 20f;

    private readonly MenuItem _jump = new(Anchor.Center, new Vector2(0f, -1.5f * ItemSpacing), "");
    private readonly MenuItem _shoot = new(Anchor.Center, new Vector2(0f, -0.5f * ItemSpacing), "");
    private readonly MenuItem _sound = new(Anchor.Center, new Vector2(0f, 0.5f * ItemSpacing), "");
    private readonly MenuItem _back = new(Anchor.Center, new Vector2(0f, 1.5f * ItemSpacing), "Back");

    private readonly FocusNavigator _navigator;

    // Read once at start and kept: every change mutates this object, writes it and applies its one
    // effect right there.
    private GameSettings _settings = new();

    // The item a capture is listening for, or null while not capturing.
    private MenuItem? _listening;

    private InputDevice _shownDevice;

    public OptionsMenu()
        : base(Anchor.Center, Vector2.Zero)
    {
        _navigator = new FocusNavigator(GameInput.MenuFocus, _jump.Focusable, _shoot.Focusable, _sound.Focusable, _back.Focusable);
        Add(_navigator);

        _jump.Pressed += () => Listen(_jump);
        _shoot.Pressed += () => Listen(_shoot);
        _sound.Pressed += ToggleSound;
        _back.Pressed += Leave;
    }

    // The items are anchored to the canvas's centre rather than to this entity, so they are the
    // scene's peers, the same shape TitleMenu adds its own items in.
    /// <inheritdoc/>
    protected override void OnAddedToScene()
    {
        Scene.Add(_jump);
        Scene.Add(_shoot);
        Scene.Add(_sound);
        Scene.Add(_back);
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        _settings = Run.Saves.Read(GameSaves.Settings);
        Refresh(InputDevice.KeyboardMouse);
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.Title);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (_listening is { } item)
        {
            if (context.Input.WasPressed(GameInput.Back))
            {
                EndListening(context.Input.ActiveDevice);
            }
            else if (context.Input.WasAnyPressed(out InputButton button))
            {
                Capture(item, button);
                EndListening(context.Input.ActiveDevice);
            }

            return;
        }

        if (context.Input.WasPressed(GameInput.Back))
        {
            Leave();

            return;
        }

        if (context.Input.ActiveDevice != _shownDevice)
        {
            Refresh(context.Input.ActiveDevice);
        }
    }

    private void Listen(MenuItem item)
    {
        _listening = item;
        _navigator.Interactable = false;
        item.Caption = item == _jump ? "Jump: press a button" : "Shoot: press a button";
    }

    private void EndListening(InputDevice device)
    {
        _listening = null;
        _navigator.Interactable = true;
        Refresh(device);
    }

    // A save moment: the document is written where the player changed it, and the bind is applied
    // from the same value so the run does not wait for a restart.
    private void Capture(MenuItem item, InputButton button)
    {
        if (item == _jump)
        {
            _settings.Input.Jump = _settings.Input.Jump.With(button);
            Run.Input.Bindings.Rebind(GameInput.Jump, _settings.Input.Jump.Key, _settings.Input.Jump.Pad);
        }
        else
        {
            _settings.Input.Shoot = _settings.Input.Shoot.With(button);
            Run.Input.Bindings.Rebind(GameInput.Shoot, _settings.Input.Shoot.Key, _settings.Input.Shoot.Pad);
        }

        Run.Saves.Write(GameSaves.Settings, _settings);
    }

    private void ToggleSound()
    {
        _settings.SoundOn = !_settings.SoundOn;
        Run.Saves.Write(GameSaves.Settings, _settings);

        // A click must not ramp; music must not pop.
        Run.Audio.SetVolume(AudioBuses.Sfx, _settings.SoundOn ? 1f : 0f);
        Run.Audio.FadeVolume(AudioBuses.Music, _settings.SoundOn ? 1f : 0f, 0.2f);

        _sound.Caption = _settings.SoundOn ? "Sound: On" : "Sound: Off";
    }

    private void Leave() => Run.RequestScene<MainMenu>();

    private void Refresh(InputDevice device)
    {
        _shownDevice = device;

        _jump.Caption = $"Jump: {DisplayName(_settings.Input.Jump.For(device))}";
        _shoot.Caption = $"Shoot: {DisplayName(_settings.Input.Shoot.For(device))}";
        _sound.Caption = _settings.SoundOn ? "Sound: On" : "Sound: Off";
    }

    // A mouse button is prefixed, since a player does not read "Left" alone as a device.
    private static string DisplayName(InputButton button) =>
        button.ToString().StartsWith($"{nameof(MouseButton)}.", StringComparison.Ordinal) ? $"Mouse {button.Name}" : button.Name;
}
