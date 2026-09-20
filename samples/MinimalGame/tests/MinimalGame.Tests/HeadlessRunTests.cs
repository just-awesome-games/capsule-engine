using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Scenes;
using Capsule.Scenes.Generated;
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
    public void ConfirmingStart_EntersTheRoomAndQuitReturnsTheExit()
    {
        StartThenQuit driver = new();

        HeadlessRunResult result = CapsuleEngine.Configure("Minimal Game", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithRunStart(GameBoot.Start)
            .WithoutLogging()
            .RunHeadless<MainMenu>(driver);

        Assert.True(driver.EnteredRoom, "confirming Start never opened the room");
        Assert.True(result.ExitRequested, "the run ended on the driver's budget, not on the game's exit");
        Assert.True(result.Steps < StartThenQuit.Budget);
    }

    // The driver `--driver Walkthrough` names is an ordinary object, so the test hands the same one
    // to the same run the shell boots. It ends on Quit rather than by running out of script, which
    // is what closes a windowed run by itself: a run that only ran dry would report no exit.
    [Fact]
    public void Walkthrough_PlaysTheRoomHeadlessAndEndsOnQuit()
    {
        HeadlessRunResult result = CapsuleEngine.Configure("Minimal Game", new DesktopPlatform(), CapsuleScenes.Registry)
            .WithRunStart(GameBoot.Start)
            .WithoutLogging()
            .RunHeadless<Room>(new Walkthrough());

        Assert.True(result.ExitRequested, "the walkthrough ran out of script before it pressed Quit");
    }

    // Presses Confirm on the menu, which opens focused on Start, then Quit as soon as the room is
    // the scene about to step. The budget is a floor under a transition that never comes.
    private sealed class StartThenQuit : IInputDriver
    {
        public const int Budget = 60;

        public bool EnteredRoom { get; private set; }

        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            if (scene is Room)
            {
                EnteredRoom = true;
                snapshot = DeviceSnapshot.Of(Key.Escape);

                return true;
            }

            snapshot = tick == 0 ? DeviceSnapshot.Of(Key.Enter) : DeviceSnapshot.Empty;

            return tick < Budget;
        }
    }
}
