using Capsule.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// Writes the run's settled rumble level to the pad. The only place pad motors are driven, and the
// owner of every hazard around them: a lost focus, a swapped player index, an exit and a crash all
// leave the motors at rest. One instance per windowed host. A headless run constructs none.
internal sealed class GamepadRumble(GamepadRumble.Writer write)
{
    // How long a non-zero level stands before it is written again. Some pads time a long effect out.
    private const double RefreshSeconds = 1.0;

    private readonly Writer _write = write;

    // What the pad was last told, and on which player slot. The level is Zero before the first
    // write, which is also the pad's state before the run.
    private RumbleLevel _lastLevel;
    private int _lastPlayer;
    private double _sinceWrite;

    // The seam between the applier and the pad. The player is the backend's slot, 0 to 3, so a
    // test drives this with no pad and no substrate type.
    internal delegate void Writer(int player, RumbleLevel level);

    // The production writer. The four-motor overload maps to the controller's rumble and trigger
    // rumble calls, and a pad without impulse triggers plays the two main motors alone. A pad-shaped
    // device with no motor at all is not written to.
    internal static void WriteToPad(int player, RumbleLevel level)
    {
        PlayerIndex index = (PlayerIndex)player;
        GamePadCapabilities capabilities = GamePad.GetCapabilities(index);
        if (!capabilities.HasLeftVibrationMotor && !capabilities.HasRightVibrationMotor)
        {
            return;
        }

        GamePad.SetVibration(index, level.Low, level.High, level.LeftTrigger, level.RightTrigger);
    }

    // Called once per frame after the steps have run. The target is the level while the window has
    // focus, a pad is connected and the pad is the active device, and Zero otherwise. A write goes
    // out when the target changes, or when a non-zero level has stood for RefreshSeconds. Nothing
    // here reaches the simulation.
    internal void Apply(RumbleLevel level, bool focused, bool connected, bool padActive, int player, double elapsedSeconds)
    {
        RumbleLevel target = focused && connected && padActive ? level : RumbleLevel.Zero;

        // The old slot is silenced before the new one is driven, or a pad unplugged and replaced on
        // another index keeps buzzing on the first.
        if (player != _lastPlayer && !_lastLevel.IsZero)
        {
            _write(_lastPlayer, RumbleLevel.Zero);
            _lastLevel = RumbleLevel.Zero;
        }

        _sinceWrite += elapsedSeconds;

        bool refresh = !_lastLevel.IsZero && _sinceWrite >= RefreshSeconds;
        if (target == _lastLevel && !refresh)
        {
            return;
        }

        _write(player, target);
        _lastLevel = target;
        _lastPlayer = player;
        _sinceWrite = 0.0;
    }

    // Rests the motors the last write drove and forgets it. Safe to call any number of times.
    internal void Silence()
    {
        if (_lastLevel.IsZero)
        {
            return;
        }

        _write(_lastPlayer, RumbleLevel.Zero);
        _lastLevel = RumbleLevel.Zero;
        _sinceWrite = 0.0;
    }
}
