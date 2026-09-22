using Capsule.Audio;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Drivers;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Tests;

// The whole game through CapsuleEngine.RunHeadless: the same builder the shell boots with, minus
// the window, so scene transitions and the exit request are the game's own. A driver that reads
// the scene stands in for the player; InputScript cannot, because what it presses depends on which
// scene is up.
public sealed class HeadlessRunTests
{
    [Fact]
    public void ConfirmingStart_EntersPlayAndQuitReturnsTheExit()
    {
        StartThenQuit driver = new();

        HeadlessRunResult result = CapsuleEngine.Configure("Minimal Game", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithRunStart(GameBoot.Start)
            .WithoutLogging()
            .RunHeadless<MainMenu>(driver);

        Assert.True(driver.EnteredPlay, "confirming Start never opened play");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on the game's exit");
        Assert.True(result.Steps < StartThenQuit.Budget);
    }

    // The driver `--driver Walkthrough` names is an ordinary object, so the test hands the same one
    // to the same run the shell boots. It ends on Quit rather than by running out of script, which
    // is what closes a windowed run by itself: a run that only ran dry would report no exit. The room
    // has no class of its own. This run also proves a document naming a base and a camera loads and plays.
    [Fact]
    public void Walkthrough_PlaysTheRoomHeadlessAndEndsOnQuit()
    {
        HeadlessRunResult result = CapsuleEngine.Configure("Minimal Game", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithRunStart(GameBoot.Start)
            .WithoutLogging()
            .RunHeadless(CapsuleAssets.Scenes.Room, new Walkthrough());

        Assert.True(result.ExitRequested, "the walkthrough ran out of script before it pressed Quit");
    }

    // Presses Confirm on the menu, then waits in play until the crossfade the menu started has
    // taken the theme off the mixer, and presses Quit on that step.
    [Fact]
    public void ConfirmingStart_CrossfadesTheMenuThemeIntoTheRoomTheme()
    {
        CrossFadesTheMenuThemeIntoTheRoomTheme driver = new();

        HeadlessRunResult result = CapsuleEngine.Configure("Minimal Game", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithRunStart(GameBoot.Start)
            .WithoutLogging()
            .RunHeadless<MainMenu>(driver);

        Assert.True(driver.TitleDied, "the title voice was still live when the driver gave up");
        Assert.True(driver.RoomIsPlaying, "the room's own loop was not sounding once the crossfade landed");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on Quit");
        Assert.True(result.Steps < CrossFadesTheMenuThemeIntoTheRoomTheme.Budget);
    }

    // Reproduces the debug overlay's jump straight from play back to the menu, which used to
    // leave the room loop playing under a fresh title, then double the room loop on the next Start.
    [Fact]
    public void JumpingFromPlayToTheMenuAndBackInsideOneRun_DoesNotDoubleTheRoomLoop()
    {
        JumpsFromPlayToMenuAndBack driver = new();

        HeadlessRunResult result = CapsuleEngine.Configure("Minimal Game", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithRunStart(GameBoot.Start)
            .WithoutLogging()
            .RunHeadless<MainMenu>(driver);

        Assert.True(driver.RoomIsPlaying, "the room's own loop was not sounding after the second entry");
        Assert.True(driver.ExactlyOneVoiceIsLive, "the jump left two room voices alive, or none");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on Quit");
        Assert.True(result.Steps < JumpsFromPlayToMenuAndBack.Budget);
    }

    // Presses Confirm on the menu, which opens focused on Start, then Quit as soon as a playable scene
    // is the scene about to step. The budget is a floor under a transition that never comes.
    private sealed class StartThenQuit : IInputDriver
    {
        public const int Budget = 60;

        public bool EnteredPlay { get; private set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            if (scene is PlayableScene)
            {
                EnteredPlay = true;
                snapshot = DeviceSnapshot.Of(Key.Escape);

                return true;
            }

            snapshot = tick == 0 ? DeviceSnapshot.Of(Key.Enter) : DeviceSnapshot.Empty;

            return tick < Budget;
        }
    }

    // Confirms Start, then holds in play polling the mixer, so the assertion tracks the
    // crossfade's own landing tick rather than one computed here.
    private sealed class CrossFadesTheMenuThemeIntoTheRoomTheme : IInputDriver
    {
        public const int Budget = 200;

        private Voice _title;

        public bool TitleDied { get; private set; }

        public bool RoomIsPlaying { get; private set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            if (scene is MainMenu)
            {
                _title = scene.Run.Game.Music.Voice;
                snapshot = tick == 0 ? DeviceSnapshot.Of(Key.Enter) : DeviceSnapshot.Empty;

                return tick < Budget;
            }

            if (scene is PlayableScene)
            {
                if (scene.Run.Audio.IsLive(_title))
                {
                    snapshot = DeviceSnapshot.Empty;

                    return tick < Budget;
                }

                TitleDied = true;
                RoomIsPlaying = scene.Run.Game.Music.IsPlaying(CapsuleAssets.Audio.Music.Room);
                snapshot = DeviceSnapshot.Of(Key.Escape);

                return true;
            }

            snapshot = DeviceSnapshot.Empty;

            return tick < Budget;
        }
    }

    // Confirms Start, then in play jumps straight back to the menu the way the debug overlay
    // does, bypassing the death path's own Stop. Confirms Start a second time, then waits for the
    // first room voice to die before checking the second one plays alone.
    private sealed class JumpsFromPlayToMenuAndBack : IInputDriver
    {
        public const int Budget = 300;

        private bool _jumped;
        private bool _confirmedAgain;
        private Voice _firstRoom;
        private Voice _secondRoom;

        public bool RoomIsPlaying { get; private set; }

        public bool ExactlyOneVoiceIsLive { get; private set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            if (scene is MainMenu)
            {
                if (!_jumped)
                {
                    snapshot = tick == 0 ? DeviceSnapshot.Of(Key.Enter) : DeviceSnapshot.Empty;
                }
                else if (!_confirmedAgain)
                {
                    _confirmedAgain = true;
                    snapshot = DeviceSnapshot.Of(Key.Enter);
                }
                else
                {
                    snapshot = DeviceSnapshot.Empty;
                }

                return tick < Budget;
            }

            if (scene is PlayableScene)
            {
                GameInstance game = scene.Run.Game;

                if (!_jumped)
                {
                    _firstRoom = game.Music.Voice;
                    _jumped = true;
                    scene.Run.RequestScene<MainMenu>();
                    snapshot = DeviceSnapshot.Empty;

                    return tick < Budget;
                }

                if (_secondRoom.IsNone)
                {
                    _secondRoom = game.Music.Voice;
                }

                if (scene.Run.Audio.IsLive(_firstRoom))
                {
                    snapshot = DeviceSnapshot.Empty;

                    return tick < Budget;
                }

                RoomIsPlaying = game.Music.IsPlaying(CapsuleAssets.Audio.Music.Room);
                ExactlyOneVoiceIsLive = scene.Run.Audio.IsLive(_firstRoom) != scene.Run.Audio.IsLive(_secondRoom);
                snapshot = DeviceSnapshot.Of(Key.Escape);

                return true;
            }

            snapshot = DeviceSnapshot.Empty;

            return tick < Budget;
        }
    }
}
