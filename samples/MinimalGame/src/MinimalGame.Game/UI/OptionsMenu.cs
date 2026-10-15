using System.Numerics;
using Capsule;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The options screen's menu: a panel showing three and a half rows of a scrolling column of Jump and Shoot
/// rebinding, the music and effects levels and Back, with a <see cref="ScrollBar"/> beside it, over one
/// <see cref="FocusNavigator"/>.
/// </summary>
public sealed class OptionsMenu : ScreenEntity
{
    // Canvas pixels between the list and its scroll bar.
    private const float BarGap = 4f;

    // What a row shows at rest, before the prompt that follows the active device and a rebind on its own.
    private const string JumpCaption = "Jump:";
    private const string ShootCaption = "Shoot:";

    // The longest caption a row shows, which the panel is sized for.
    private const string ShootPrompt = "Shoot: press a button";

    private readonly MenuItem _jump = new(JumpCaption, GameInput.Jump);
    private readonly MenuItem _shoot = new(ShootCaption, GameInput.Shoot);
    private readonly VolumeSlider _music = new("Music");
    private readonly VolumeSlider _effects = new("Effects");
    private readonly MenuItem _back = new("Back");

    private readonly FocusNavigator _navigator;

    // Read once at start and kept: every change mutates this object, writes it and applies its one
    // effect right there.
    private GameSettings _settings = new();

    // The item a capture is listening for, or null while not capturing.
    private MenuItem? _listening;

    public OptionsMenu()
        : base(Anchor.Center, Vector2.Zero)
    {
        // Wide enough for the longest caption a capture shows, and only half of the fourth row shows, which
        // tells the player there is more below.
        float listWidth = Math.Max(Math.Max(_music.Size.X, _effects.Size.X), MenuItem.WidthFor(ShootPrompt));
        Size = new Vector2(listWidth + BarGap + ScrollBar.Width, 3.5f * (MenuItem.BoxHeight + MenuItem.Spacing));

        ScrollContainer list = new(Anchor.Fill, Vector2.Zero) { Parent = this, Padding = new Insets(0f, 0f, ScrollBar.Width + BarGap, 0f) };
        BoxContainer column = new(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = list, Spacing = MenuItem.Spacing };

        _jump.Parent = column;
        _shoot.Parent = column;
        _music.Parent = column;
        _effects.Parent = column;
        _back.Parent = column;

        _ = new ScrollBar(list) { Parent = this };

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
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.TitleSound);
    }

    // Back cancels a capture here. The navigator reads nothing while a capture listens or on the step
    // it turns back on, and the same press never also leaves the screen.
    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (_listening is not { } item)
        {
            return;
        }

        if (context.Input.WasPressed(GameInput.Back))
        {
            EndListening(item);
        }
        else if (context.Input.WasAnyPressed(out InputButton button))
        {
            Capture(item, button);
            EndListening(item);
        }
    }

    private void Listen(MenuItem item)
    {
        _listening = item;
        _navigator.Interactable = false;
        item.Caption = item == _jump ? "Jump: press a button" : ShootPrompt;
        item.ShowsPrompt = false;
    }

    private void EndListening(MenuItem item)
    {
        item.Caption = item == _jump ? JumpCaption : ShootCaption;
        item.ShowsPrompt = true;
        _listening = null;
        _navigator.Interactable = true;
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
}
