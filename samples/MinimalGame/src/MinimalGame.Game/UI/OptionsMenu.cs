using MinimalGame.Game.Scenes;

namespace MinimalGame.Game.UI;

/// <summary>
/// The options screen's menu: a panel showing three and a half rows of a scrolling column of Jump and Shoot
/// rebinding, the music and effects levels, the display settings, the language and Back, with a
/// <see cref="ScrollBar"/> beside it, over one <see cref="FocusNavigator"/>.
/// </summary>
public sealed class OptionsMenu : ScreenEntity
{
    // Canvas pixels between the list and its scroll bar.
    private const float BarGap = 4f;

    // What the Window and Frame cap rows step through. A press moves to the next and wraps.
    private const int SmallestWindowScale = 2;
    private const int LargestWindowScale = 6;
    private static readonly int[] FrameCaps = [0, 30, 60, 120, 144, 240];

    private readonly MenuItem _jump = new(CapsuleStrings.Options.Jump, GameInput.Jump);
    private readonly MenuItem _shoot = new(CapsuleStrings.Options.Shoot, GameInput.Shoot);
    private readonly VolumeSlider _music = new(CapsuleStrings.Options.Music);
    private readonly VolumeSlider _effects = new(CapsuleStrings.Options.Effects);

    // Captioned by ShowDisplay from the settings.
    private readonly MenuItem _fullscreen = new();
    private readonly MenuItem _windowSize = new();
    private readonly MenuItem _vsync = new();
    private readonly MenuItem _frameCap = new();

    private readonly MenuItem _language = new(CapsuleStrings.Options.Language);

    private readonly MenuItem _back = new(CapsuleStrings.Menu.Back);

    // Every row but the sliders, which the panel's width fits with them.
    private readonly MenuItem[] _items;

    private readonly FocusNavigator _navigator;

    // Read once at start and kept: every change mutates this object, writes it and applies its one
    // effect right there.
    private GameSettings _settings = new();

    // The item a capture is listening for, or null while not capturing.
    private MenuItem? _listening;

    public OptionsMenu()
        : base(Anchor.Center, Vector2.Zero)
    {
        _items = [_jump, _shoot, _fullscreen, _windowSize, _vsync, _frameCap, _language, _back];

        ScrollContainer list = new(Anchor.Fill, Vector2.Zero) { Parent = this, Padding = new Insets(0f, 0f, ScrollBar.Width + BarGap, 0f) };
        BoxContainer column = new(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = list, Spacing = MenuItem.Spacing };

        _jump.Parent = column;
        _shoot.Parent = column;
        _music.Parent = column;
        _effects.Parent = column;
        _fullscreen.Parent = column;
        _windowSize.Parent = column;
        _vsync.Parent = column;
        _frameCap.Parent = column;
        _language.Parent = column;
        _back.Parent = column;

        _ = new ScrollBar(list) { Parent = this };

        _navigator = new FocusNavigator(GameInput.MenuFocus);
        Add(_navigator);

        _language.Pressed += NextLanguage;
        _jump.Pressed += () => Listen(_jump);
        _shoot.Pressed += () => Listen(_shoot);

        // Reset is the menu's only secondary action.
        _jump.SecondaryPressed += _ => Apply(_jump, new InputSettings().Jump);
        _shoot.SecondaryPressed += _ => Apply(_shoot, new InputSettings().Shoot);

        _music.Changed += _ => SetVolume(AudioBuses.Music, _music);
        _effects.Changed += _ => SetVolume(AudioBuses.Sfx, _effects);
        _fullscreen.Pressed += () => ChangeDisplay(static settings => settings.Fullscreen = !settings.Fullscreen);
        _windowSize.Pressed += () => ChangeDisplay(static settings => settings.WindowScale = settings.WindowScale >= LargestWindowScale ? SmallestWindowScale : settings.WindowScale + 1);
        _vsync.Pressed += () => ChangeDisplay(static settings => settings.VSync = !settings.VSync);
        _frameCap.Pressed += () => ChangeDisplay(static settings => settings.MaxFrameRate = FrameCaps[(Array.IndexOf(FrameCaps, settings.MaxFrameRate) + 1) % FrameCaps.Length]);
        _back.Pressed += Leave;
        _navigator.Canceled += Leave;
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        _settings = Run.Saves.Read(GameSaves.Settings);
        _music.Value = _settings.MusicVolume;
        _effects.Value = _settings.EffectsVolume;
        ShowDisplay();
        Fit();
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.TitleSound);
    }

    // The rows' captions follow the language, and a capture's caption is wider than the row at rest.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context) => Fit();

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
            Apply(item, (item == _jump ? _settings.Input.Jump : _settings.Input.Shoot).With(button));
            EndListening(item);
        }
    }

    private void Listen(MenuItem item)
    {
        _listening = item;
        _navigator.Interactable = false;
        item.SetCaption(item == _jump ? CapsuleStrings.Options.JumpListening : CapsuleStrings.Options.ShootListening);
        item.ShowsPrompt = false;
    }

    private void EndListening(MenuItem item)
    {
        item.SetCaption(item == _jump ? CapsuleStrings.Options.Jump : CapsuleStrings.Options.Shoot);
        item.ShowsPrompt = true;
        _listening = null;
        _navigator.Interactable = true;
    }

    // A save moment: the document is written where the player changed it, and the bind is applied
    // from the same value so the run does not wait for a restart.
    private void Apply(MenuItem item, Binding binding)
    {
        if (item == _jump)
        {
            _settings.Input.Jump = binding;
            Run.Input.Rebind(GameInput.Jump, binding.Key, binding.Pad);
        }
        else
        {
            _settings.Input.Shoot = binding;
            Run.Input.Rebind(GameInput.Shoot, binding.Key, binding.Pad);
        }

        Run.Saves.Write(GameSaves.Settings, _settings);
    }

    // A save moment like Apply: every caption shows the next language from the next step.
    private void NextLanguage()
    {
        ReadOnlySpan<string> languages = CapsuleStrings.Languages;
        int shown = Run.Language is { } language ? languages.IndexOf(language) : -1;
        _settings.Language = languages[(shown + 1) % languages.Length];
        Run.Language = _settings.Language;
        Run.Saves.Write(GameSaves.Settings, _settings);
    }

    // A save moment like Apply: the document takes both levels from their sliders, and the moved
    // slider's bus takes its level at once.
    private void SetVolume(AudioBus bus, VolumeSlider slider)
    {
        _settings.MusicVolume = _music.Value;
        _settings.EffectsVolume = _effects.Value;
        Run.Saves.Write(GameSaves.Settings, _settings);
        Run.Audio.SetVolume(bus, slider.Value);
    }

    // A save moment like Apply: the run takes the changed display from the same document it is
    // written from.
    private void ChangeDisplay(Action<GameSettings> change)
    {
        change(_settings);
        _settings.ApplyDisplay(Run.Display);
        Run.Saves.Write(GameSaves.Settings, _settings);
        ShowDisplay();
    }

    private void ShowDisplay()
    {
        _fullscreen.SetCaption(_settings.Fullscreen ? CapsuleStrings.Options.Fullscreen : CapsuleStrings.Options.Windowed);
        _windowSize.SetCaption(CapsuleStrings.Options.WindowScale, _settings.WindowScale);
        _vsync.SetCaption(_settings.VSync ? CapsuleStrings.Options.VsyncOn : CapsuleStrings.Options.VsyncOff);
        if (_settings.MaxFrameRate == 0)
        {
            _frameCap.SetCaption(CapsuleStrings.Options.FrameCapOff);
        }
        else
        {
            _frameCap.SetCaption(CapsuleStrings.Options.FrameCap, _settings.MaxFrameRate);
        }
    }

    // As wide as the widest row shows now, and tall enough that only half of the fourth row shows, which
    // tells the player there is more below.
    private void Fit()
    {
        float widest = Math.Max(_music.ContentWidth, _effects.ContentWidth);
        foreach (MenuItem item in _items)
        {
            widest = Math.Max(widest, item.ContentWidth);
        }

        Vector2 size = new(widest + BarGap + ScrollBar.Width, 3.5f * (MenuItem.BoxHeight + MenuItem.Spacing));
        if (Size != size)
        {
            Size = size;
        }
    }

    private void Leave() => Run.RequestScene<MainMenu>();
}
