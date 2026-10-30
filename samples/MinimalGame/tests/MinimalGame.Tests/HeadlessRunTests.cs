using System.Numerics;
using Capsule.Audio;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Drivers;
using MinimalGame.Game.Entities;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Tests;

// The whole game through EngineBuilder.RunHeadless: the shell's own CapsuleBoot builder, minus
// the window. Scene transitions and the exit request are the game's own. A driver that reads the
// scene stands in for the player. InputScript cannot, because what it presses depends on which
// scene is up.
public sealed class HeadlessRunTests
{
    [Fact]
    public void ConfirmingStart_EntersPlayAndQuitReturnsTheExit()
    {
        StartThenQuit driver = new();

        HeadlessRunResult result = Boot().RunHeadless<MainMenu>(driver);

        Assert.True(driver.EnteredPlay, "confirming Start never opened play");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on the game's exit");
        Assert.True(result.Steps < driver.Budget);
    }

    // A window focus loss in play opens the pause menu as Escape does. Regaining window focus on the
    // next step leaves the menu open, and the same Down and Confirm quit from it.
    [Fact]
    public void LosingWindowFocusInPlay_OpensThePauseMenu()
    {
        HeadlessRunResult result = Boot().RunHeadless<MainMenu>(new StartThenQuit(DeviceSnapshot.Empty.WithWindowFocus(false)));

        Assert.True(result.ExitRequested, "the window focus loss did not open the pause menu");
    }

    // The shell's own driver, handed to the same run the shell boots. It ends on the pause menu's Quit,
    // and a run that only ran out of script would report no exit. The room is a document naming a base
    // scene and a camera, with no class of its own.
    [Fact]
    public void Walkthrough_PlaysTheRoomHeadlessAndEndsOnQuit()
    {
        HeadlessRunResult result = Boot().RunHeadless(CapsuleAssets.Scenes.RoomScene, new Walkthrough());

        Assert.True(result.ExitRequested, "the walkthrough ran out of script before it pressed Quit");
    }

    // Presses Confirm on the menu, then waits in play until the crossfade the menu started has
    // taken the theme off the mixer, and quits through the pause menu from that step.
    [Fact]
    public void ConfirmingStart_CrossfadesTheMenuThemeIntoTheRoomTheme()
    {
        CrossFadesTheMenuThemeIntoTheRoomTheme driver = new();

        HeadlessRunResult result = Boot().RunHeadless<MainMenu>(driver);

        Assert.True(driver.TitleDied, "the title voice was still live when the driver gave up");
        Assert.True(driver.RoomIsPlaying, "the room's own loop was not sounding once the crossfade landed");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on Quit");
        Assert.True(result.Steps < driver.Budget);
    }

    // A jump straight from play back to the menu, as the debug overlay makes, lets the room loop fade
    // under the title. The next Start plays one room loop, not two.
    [Fact]
    public void JumpingFromPlayToTheMenuAndBackInsideOneRun_DoesNotDoubleTheRoomLoop()
    {
        JumpsFromPlayToMenuAndBack driver = new();

        HeadlessRunResult result = Boot().RunHeadless<MainMenu>(driver);

        Assert.True(driver.RoomIsPlaying, "the room's own loop was not sounding after the second entry");
        Assert.True(driver.ExactlyOneVoiceIsLive, "the jump left two room voices alive, or none");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on Quit");
        Assert.True(result.Steps < driver.Budget);
    }

    // The shell's command line with a preset arrival. The hall opens with the player on its west
    // entrance, not its own spawn, before the first step. The probe replaces the --driver it names.
    [Fact]
    public void ANamedPayload_BootsTheHallWithThePlayerAtItsEntrance()
    {
        ArrivalProbe probe = new();

        Boot()
            .WithCommandLine(["--scene", "scenes/halls/hall", "--payload", nameof(WestArrival), "--driver", "Walkthrough", "--headless"])
            .WithInputDriver(probe)
            .RunScene<MainMenu>();

        Assert.NotNull(probe.EntranceAt);
        Assert.Equal(probe.EntranceAt, probe.PlayerAt);
    }

    // The shell's builder, render resolution included, which a recording made in the shell checks.
    internal static EngineBuilder Boot() =>
        CapsuleBoot.Configure("Minimal Game")
            .WithRunStart(GameBoot.Start)
            .WithRunEnd(GameBoot.End)
            .WithRenderResolution((int)World.ViewportSize.X, (int)World.ViewportSize.Y)
            .WithoutLogging();

    // Presses Confirm on the menu, which opens focused on Start, and asks QuitsFrom each step in play
    // until it says to quit. The budget is a floor under a transition that never comes.
    private class StartThenQuit(DeviceSnapshot? pause = null, int budget = 60) : IInputDriver
    {
        private int _presses;

        public int Budget => budget;

        public bool EnteredPlay { get; private set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;
            if (scene is MainMenu menu && ConfirmsOn(menu, tick))
            {
                snapshot = DeviceSnapshot.Of(Key.Enter);
            }
            else if (scene is PlayableScene play)
            {
                EnteredPlay = true;
                if (_presses > 0 || QuitsFrom(play))
                {
                    snapshot = NextQuitPress();

                    return true;
                }
            }

            return tick < budget;
        }

        // Whether this step on the menu presses Confirm. The first step does.
        protected virtual bool ConfirmsOn(MainMenu menu, long tick) => tick == 0;

        // Whether play has reached what the test waits for. Asked each step in play until it has.
        protected virtual bool QuitsFrom(PlayableScene scene) => true;

        // Quits from play the way a player does, one press a step: Pause opens the menu on Resume, Down
        // moves the focus to Quit, and Confirm picks it. The pause settles on the step after the press,
        // which is the first step the menu reads input on. A test may open the menu another way.
        private DeviceSnapshot NextQuitPress() => _presses++ switch
        {
            0 => pause ?? DeviceSnapshot.Of(Key.Escape),
            1 => DeviceSnapshot.Of(Key.Down),
            _ => DeviceSnapshot.Of(Key.Enter),
        };
    }

    // Confirms Start, then holds in play polling the mixer. The assertion tracks the crossfade's own
    // landing tick rather than one computed here.
    private sealed class CrossFadesTheMenuThemeIntoTheRoomTheme() : StartThenQuit(budget: 200)
    {
        private Voice _title;

        public bool TitleDied { get; private set; }

        public bool RoomIsPlaying { get; private set; }

        protected override bool ConfirmsOn(MainMenu menu, long tick)
        {
            _title = menu.Run.Game.Music.Voice;

            return tick == 0;
        }

        protected override bool QuitsFrom(PlayableScene scene)
        {
            if (scene.Run.Audio.IsLive(_title))
            {
                return false;
            }

            TitleDied = true;
            RoomIsPlaying = scene.Run.Game.Music.IsPlaying(CapsuleAssets.Audio.Music.RoomSound);

            return true;
        }
    }

    // Confirms Start, then in play jumps straight back to the menu the way the debug overlay
    // does, bypassing the death path's own Stop. Confirms Start a second time, then waits for the
    // first room voice to die before checking the second one plays alone.
    private sealed class JumpsFromPlayToMenuAndBack() : StartThenQuit(budget: 300)
    {
        private bool _jumped;
        private bool _confirmedAgain;
        private Voice _firstRoom;
        private Voice _secondRoom;

        public bool RoomIsPlaying { get; private set; }

        public bool ExactlyOneVoiceIsLive { get; private set; }

        protected override bool ConfirmsOn(MainMenu menu, long tick)
        {
            if (!_jumped)
            {
                return tick == 0;
            }

            bool first = !_confirmedAgain;
            _confirmedAgain = true;

            return first;
        }

        protected override bool QuitsFrom(PlayableScene scene)
        {
            GameInstance game = scene.Run.Game;

            if (!_jumped)
            {
                _firstRoom = game.Music.Voice;
                _jumped = true;
                scene.Run.RequestScene<MainMenu>();

                return false;
            }

            if (_secondRoom.IsNone)
            {
                _secondRoom = game.Music.Voice;
            }

            if (scene.Run.Audio.IsLive(_firstRoom))
            {
                return false;
            }

            RoomIsPlaying = game.Music.IsPlaying(CapsuleAssets.Audio.Music.RoomSound);
            ExactlyOneVoiceIsLive = scene.Run.Audio.IsLive(_firstRoom) != scene.Run.Audio.IsLive(_secondRoom);

            return true;
        }
    }

    // Reads where the player and the west entrance stand as the first step is asked for, then ends the run.
    private sealed class ArrivalProbe : IInputDriver
    {
        public Vector2? PlayerAt { get; private set; }

        public Vector2? EntranceAt { get; private set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            PlayerAt = scene.FindSingle<Player>().Position;
            EntranceAt = scene.FindFirst<Entrance>(entrance => entrance.From == CapsuleAssets.Scenes.RoomScene)?.Position;
            snapshot = DeviceSnapshot.Empty;

            return false;
        }
    }
}
