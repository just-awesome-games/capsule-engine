using Capsule.Input;
using Capsule.Runtime.Sdl;

namespace Capsule.Runtime.Input;

// Writes the run's settled rumble level to the pad. The only place pad motors are driven, and the
// owner of every hazard around them: a lost focus, a change of active pad, a held run, an exit and a
// crash all leave the motors at rest. One instance per windowed host. A headless run constructs none.
internal sealed class GamepadRumble(GamepadRumble.Writer write)
{
    // How long a non-zero level stands before it is written again, well inside each write's duration.
    private const double RefreshSeconds = 1.0;

    // Each write plays for two refreshes, and a standing level stays continuous. SDL ends a write the
    // refresh does not renew on its next update. A stalled game thread stops those updates too, and a
    // pad then holds its last speed until they resume.
    private const uint DurationMs = (uint)(RefreshSeconds * 2000);

    private readonly Writer _write = write;

    // What the pad was last told, and which pad. The level is Zero before the first write, which is
    // also the pad's state before the run.
    private RumbleLevel _lastLevel;
    private uint _lastPad;
    private double _sinceWrite;

    // The seam between the applier and the pad. The pad is SDL's joystick id, 0 for none. A test drives
    // this with no pad and no SDL.
    internal delegate void Writer(uint pad, RumbleLevel level);

    // The production writer. The main and the trigger motors are each written when the pad reports
    // them. A pad that is gone is not written to.
    internal static unsafe void WriteToPad(uint pad, RumbleLevel level)
    {
        nint gamepad = Sdl3.SDL_GetGamepadFromID(pad);
        if (gamepad == nint.Zero)
        {
            return;
        }

        uint properties = Sdl3.SDL_GetGamepadProperties(gamepad);
        fixed (byte* rumble = Sdl3.GamepadCapRumble, triggers = Sdl3.GamepadCapTriggerRumble)
        {
            if (Sdl3.SDL_GetBooleanProperty(properties, rumble, defaultValue: false))
            {
                Sdl3.SDL_RumbleGamepad(gamepad, Motor(level.Low), Motor(level.High), DurationMs);
            }

            if (Sdl3.SDL_GetBooleanProperty(properties, triggers, defaultValue: false))
            {
                Sdl3.SDL_RumbleGamepadTriggers(gamepad, Motor(level.LeftTrigger), Motor(level.RightTrigger), DurationMs);
            }
        }
    }

    // Called once per frame after the steps have run. pad is the active pad, 0 with none connected.
    // The target is the level while the window has focus, a pad is connected, the pad is the active
    // device and the run is not held, and Zero otherwise. A write goes out when the target changes, or
    // when a non-zero level has stood for RefreshSeconds. Nothing here reaches the simulation.
    internal void Apply(RumbleLevel level, bool focused, bool padActive, bool runHeld, uint pad, double elapsedSeconds)
    {
        // A held run's tick stands still, and so does the level it mixes. Written through, a pulse
        // played on the last step would buzz until the hold ends. Its clock resumes with the run.
        RumbleLevel target = focused && pad != 0 && padActive && !runHeld ? level : RumbleLevel.Zero;

        // The old pad is silenced before the new one is driven, or a pad the player put down keeps
        // buzzing while another plays.
        if (pad != _lastPad && !_lastLevel.IsZero)
        {
            _write(_lastPad, RumbleLevel.Zero);
            _lastLevel = RumbleLevel.Zero;
        }

        _sinceWrite += elapsedSeconds;

        bool refresh = !_lastLevel.IsZero && _sinceWrite >= RefreshSeconds;
        if (target == _lastLevel && !refresh)
        {
            return;
        }

        _write(pad, target);
        _lastLevel = target;
        _lastPad = pad;
        _sinceWrite = 0.0;
    }

    // Rests the motors the last write drove and forgets it. Safe to call any number of times.
    internal void Silence()
    {
        if (_lastLevel.IsZero)
        {
            return;
        }

        _write(_lastPad, RumbleLevel.Zero);
        _lastLevel = RumbleLevel.Zero;
        _sinceWrite = 0.0;
    }

    private static ushort Motor(float level) => (ushort)(65535f * Math.Clamp(level, 0f, 1f));
}
