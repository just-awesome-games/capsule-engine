using Capsule.Diagnostics;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Input;

// What the devices did between two samples, recorded from SDL's events: every key, mouse button and
// pad button that went down, however briefly, and the wheel's turns. A sample reads held state, which
// misses a press released before it, and adds these.
internal sealed class InputEvents(TriggerEdges triggers)
{
    // Pads pressing in one frame past this many lose those presses. Their held buttons still read.
    internal const int MaxPressingPads = 8;

    // The press bits of the triggers, above every SDL_GamepadButton's.
    internal const uint LeftTriggerPressed = 1u << 30;
    internal const uint RightTriggerPressed = 1u << 31;

    internal const int MaxTimedPresses = 32;

    private readonly bool[] _keys = new bool[Sdl3.ScancodeCount];
    private readonly (uint Pad, uint Buttons)[] _pads = new (uint, uint)[MaxPressingPads];
    private int _padCount;

    // SDL's timestamps of the presses, read by the development plane's latency measurement. Presses
    // past this many in one frame go untimed.
    private readonly ulong[] _pressTimes = new ulong[MaxTimedPresses];
    private int _pressTimeCount;

    // Indexed by SDL scancode. A key repeat is no press.
    internal ReadOnlySpan<bool> KeysPressed => _keys;

    // MouseSampler's button bits.
    internal uint MouseButtonsPressed { get; private set; }

    // Whole wheel notches, X positive to the right and Y positive away from the user.
    internal int WheelX { get; private set; }

    internal int WheelY { get; private set; }

    // The SDL joystick id of the pad whose button or trigger went down last, or 0.
    internal uint LastPadPressed { get; private set; }

    // Whether a joystick or a gamepad was added or removed.
    internal bool PadsChanged { get; private set; }

    // Whether the keyboard layout changed, which relabels the keys.
    internal bool KeymapChanged { get; private set; }

    // Whether the window's display scale changed, or the window was restored or left fullscreen and
    // may have missed a change while its size was the display's.
    internal bool DisplayScaleMayHaveChanged { get; private set; }

    // Whether the window moved to another display or a display's mode changed, which may change the
    // refresh rate the window presents at.
    internal bool RefreshRateMayHaveChanged { get; private set; }

    // The SDL_GetTicksNS time of every key, mouse button and pad button that went down, in event order.
    // Empty unless the development plane is on.
    internal ReadOnlySpan<ulong> PressTimes => _pressTimes.AsSpan(0, _pressTimeCount);

    // The SDL_GamepadButton bits of pad that went down, and its trigger press bits.
    internal uint PadButtonsPressed(uint pad)
    {
        for (int index = 0; index < _padCount; index++)
        {
            if (_pads[index].Pad == pad)
            {
                return _pads[index].Buttons;
            }
        }

        return 0;
    }

    // Reads one SDL3 event. Every other type is ignored.
    internal unsafe void Record(byte* sdlEvent)
    {
        switch (*(uint*)sdlEvent)
        {
            case Sdl3.EventKeyDown:
                Sdl3.KeyboardEvent* key = (Sdl3.KeyboardEvent*)sdlEvent;
                if (key->Repeat == 0 && (uint)key->Scancode < Sdl3.ScancodeCount)
                {
                    _keys[key->Scancode] = true;
                    TimePress(key->Timestamp);
                }

                break;

            case Sdl3.EventMouseButtonDown:
                Sdl3.MouseButtonEvent* mouse = (Sdl3.MouseButtonEvent*)sdlEvent;
                MouseButtonsPressed |= MouseSampler.SdlButtonBit(mouse->Button);
                TimePress(mouse->Timestamp);
                break;

            // The whole notches SDL accumulates from a wheel that reports finer than a notch. A flipped
            // wheel reports both reversed.
            case Sdl3.EventMouseWheel:
                Sdl3.MouseWheelEvent* wheel = (Sdl3.MouseWheelEvent*)sdlEvent;
                int sign = wheel->Direction == Sdl3.MouseWheelFlipped ? -1 : 1;
                WheelX += sign * wheel->IntegerX;
                WheelY += sign * wheel->IntegerY;
                break;

            // A trigger is an axis to SDL. Its rise past the press threshold is a press of that pad.
            case Sdl3.EventGamepadAxisMotion:
                Sdl3.GamepadAxisEvent* axis = (Sdl3.GamepadAxisEvent*)sdlEvent;
                bool left = axis->Axis == (byte)Sdl3.GamepadAxis.LeftTrigger;
                if ((left || axis->Axis == (byte)Sdl3.GamepadAxis.RightTrigger) && triggers.Rose(axis->Which, left, axis->Value))
                {
                    PressPad(axis->Which, left ? LeftTriggerPressed : RightTriggerPressed);
                    TimePress(axis->Timestamp);
                }

                break;

            case Sdl3.EventGamepadButtonDown:
                Sdl3.GamepadButtonEvent* button = (Sdl3.GamepadButtonEvent*)sdlEvent;
                if (button->Button < 30)
                {
                    PressPad(button->Which, 1u << button->Button);
                    TimePress(button->Timestamp);
                }

                break;

            case Sdl3.EventGamepadRemoved:
                triggers.Forget(((Sdl3.GamepadDeviceEvent*)sdlEvent)->Which);
                PadsChanged = true;
                break;

            case Sdl3.EventGamepadAdded:
            case Sdl3.EventJoystickAdded:
            case Sdl3.EventJoystickRemoved:
                PadsChanged = true;
                break;

            case Sdl3.EventKeymapChanged:
                KeymapChanged = true;
                break;

            case Sdl3.EventWindowDisplayScaleChanged:
            case Sdl3.EventWindowRestored:
            case Sdl3.EventWindowLeaveFullscreen:
                DisplayScaleMayHaveChanged = true;
                break;

            case Sdl3.EventWindowDisplayChanged:
            case Sdl3.EventDisplayDesktopModeChanged:
                RefreshRateMayHaveChanged = true;
                break;
        }
    }

    internal void Clear()
    {
        Array.Clear(_keys);
        MouseButtonsPressed = 0;
        WheelX = 0;
        WheelY = 0;
        LastPadPressed = 0;
        PadsChanged = false;
        KeymapChanged = false;
        DisplayScaleMayHaveChanged = false;
        RefreshRateMayHaveChanged = false;
        _padCount = 0;
        _pressTimeCount = 0;
    }

    private void TimePress(ulong timestamp)
    {
        if (Development.IsSupported && _pressTimeCount < MaxTimedPresses)
        {
            _pressTimes[_pressTimeCount++] = timestamp;
        }
    }

    private void PressPad(uint pad, uint bit)
    {
        LastPadPressed = pad;
        for (int index = 0; index < _padCount; index++)
        {
            if (_pads[index].Pad == pad)
            {
                _pads[index].Buttons |= bit;
                return;
            }
        }

        if (_padCount < MaxPressingPads)
        {
            _pads[_padCount++] = (pad, bit);
        }
    }
}
