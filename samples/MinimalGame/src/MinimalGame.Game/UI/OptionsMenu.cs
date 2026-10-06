using System.Numerics;
using Capsule;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The options screen's menu: a column of Jump and Shoot rebinding, the music and effects levels and
/// Back, over one <see cref="FocusNavigator"/>.
/// </summary>
public sealed class OptionsMenu : BoxContainer
{
    private readonly MenuItem _jump = new("");
    private readonly MenuItem _shoot = new("");
    private readonly VolumeSlider _music = new("Music");
    private readonly VolumeSlider _effects = new("Effects");
    private readonly MenuItem _back = new("Back");

    private readonly FocusNavigator _navigator;

    // Read once at start and kept: every change mutates this object, writes it and applies its one
    // effect right there.
    private GameSettings _settings = new();

    // The item a capture is listening for, or null while not capturing.
    private MenuItem? _listening;

    private InputDevice _shownDevice;

    public OptionsMenu()
        : base(Axis.Vertical, Anchor.Center, Vector2.Zero)
    {
        Spacing = MenuItem.Spacing;

        _jump.Parent = this;
        _shoot.Parent = this;
        _music.Parent = this;
        _effects.Parent = this;
        _back.Parent = this;

        _navigator = new FocusNavigator(GameInput.MenuFocus);
        Add(_navigator);

        _jump.Pressed += () => Listen(_jump);
        _shoot.Pressed += () => Listen(_shoot);
        _music.Changed += SetMusic;
        _effects.Changed += SetEffects;
        _back.Pressed += Leave;
        _navigator.Canceled += Leave;
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        _settings = Run.Saves.Read(GameSaves.Settings);
        _music.Value = _settings.MusicVolume;
        _effects.Value = _settings.EffectsVolume;
        Refresh(InputDevice.KeyboardMouse);
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.TitleSound);
    }

    // Back cancels a capture here. The navigator reads nothing while a capture listens or on the step
    // it turns back on, and the same press never also leaves the screen.
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

    private void SetMusic(float volume)
    {
        _settings.MusicVolume = volume;
        Run.Saves.Write(GameSaves.Settings, _settings);
        Run.Audio.SetVolume(AudioBuses.Music, volume);
    }

    private void SetEffects(float volume)
    {
        _settings.EffectsVolume = volume;
        Run.Saves.Write(GameSaves.Settings, _settings);
        Run.Audio.SetVolume(AudioBuses.Sfx, volume);
    }

    private void Leave() => Run.RequestScene<MainMenu>();

    private void Refresh(InputDevice device)
    {
        _shownDevice = device;

        _jump.Caption = $"Jump: {DisplayName(_settings.Input.Jump.For(device))}";
        _shoot.Caption = $"Shoot: {DisplayName(_settings.Input.Shoot.For(device))}";
    }

    // A mouse button is prefixed, since a player does not read "Left" alone as a device.
    private static string DisplayName(InputButton button) =>
        button.ToString().StartsWith($"{nameof(MouseButton)}.", StringComparison.Ordinal) ? $"Mouse {button.Name}" : button.Name;
}
