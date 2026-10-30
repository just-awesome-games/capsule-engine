using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Input;

// Folds the active gamepad into a DeviceSnapshot. The only place pad hardware enters the engine. Every
// connected pad is opened, and PadRoster keeps the active one: a button or trigger going down on
// another pad makes that pad active and counts as its press. One instance per windowed host, which
// holds SDL's gamepad subsystem open for its lifetime. An unfocused window folds none of it but the
// active pad's family and the buttons it has, which every sample carries and which outlast the pad's
// disconnection.
internal sealed class GamepadSampler : IDisposable
{
    private static readonly (PadButton Button, Sdl3.GamepadButton Sdl)[] Mappings = BuildLookup();

    private readonly PadRoster _pads = new();
    private readonly Func<uint, Pad?> _openPad;
    private readonly Action<Pad> _closePad;
    private readonly Func<Pad, Pad> _remapPad;

    // The joysticks already reported as no gamepad, by SDL instance id.
    private readonly HashSet<uint> _unmapped = [];
    private readonly bool _open;

    // Whether the embedded controller database has been read, which happens once, for the first
    // connected joystick SDL cannot map.
    private bool _databaseRead;

    // The family of the pad last active, kept when it disconnects so a prompt does not flicker.
    private PadFamily _family;

    // The PadButton bits the pad last active lacks, kept with its family.
    private uint _lacking;

    // Pads plugged in before the watch existed raised their added events unheard. They are read from
    // SDL's list here, after the player's mappings, and every later change from the feed. openContent
    // reads a player's gamecontrollerdb.txt, and null reads none.
    internal GamepadSampler(Func<string, Stream>? openContent)
    {
        _openPad = Open;
        _closePad = Close;
        _remapPad = Remap;
        _open = Sdl3.SDL_InitSubSystem(Sdl3.InitGamepad);
        if (!_open)
        {
            Log.Warning($"No gamepad will be read: SDL's gamepad subsystem did not start: {Sdl3.Error()}");
            return;
        }

        GamepadMappings.LoadPlayerFile(openContent);
        Reconcile();
    }

    // The SDL joystick id of the active pad, or 0 with none connected. The rumble applier drives it.
    internal uint ActivePad => _pads.ActiveId;

    // A raw SDL axis reading in [-1, 1], scaled by 32768 below centre and 32767 above.
    internal static float Axis(short raw) => raw < 0 ? raw / 32768f : raw / 32767f;

    // A raw stick reading in the unit square, Y positive up.
    internal static (float X, float Y) Stick(short x, short y) =>
        (Math.Clamp(Axis(x), -1f, 1f), Math.Clamp(Axis(y) * -1f, -1f, 1f));

    // A raw trigger reading in [0, 1].
    internal static float Trigger(short raw) => Math.Clamp(Axis(raw), 0f, 1f);

    // The lowest raw trigger reading that reads pressed under deadzone, the first whose pull
    // PadFilter.Trigger does not zero, or int.MaxValue when none does.
    internal static int PressThreshold(float deadzone)
    {
        int low = 0;
        int high = short.MaxValue + 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Trigger((short)middle) > deadzone)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low > short.MaxValue ? int.MaxValue : low;
    }

    // sampled with the buttons and triggers pressed names down, in InputEvents' press bits.
    internal static DeviceSnapshot WithPresses(DeviceSnapshot sampled, uint pressed)
    {
        if (pressed == 0)
        {
            return sampled;
        }

        foreach ((PadButton button, Sdl3.GamepadButton sdl) in Mappings)
        {
            if ((pressed & (1u << (int)sdl)) != 0)
            {
                sampled = sampled.With(button);
            }
        }

        if ((pressed & InputEvents.LeftTriggerPressed) != 0)
        {
            sampled = sampled.With(PadButton.LeftTrigger);
        }

        return (pressed & InputEvents.RightTriggerPressed) != 0 ? sampled.With(PadButton.RightTrigger) : sampled;
    }

    // The family a pad of SDL's type belongs to. A Steam pad's face buttons are Xbox-lettered in Xbox
    // positions.
    internal static PadFamily Family(Sdl3.GamepadType type) => type switch
    {
        Sdl3.GamepadType.Xbox360 or Sdl3.GamepadType.XboxOne or Sdl3.GamepadType.Steam => PadFamily.Xbox,
        Sdl3.GamepadType.PS3 or Sdl3.GamepadType.PS4 or Sdl3.GamepadType.PS5 => PadFamily.PlayStation,
        Sdl3.GamepadType.NintendoSwitchPro or Sdl3.GamepadType.NintendoSwitchJoyConLeft
            or Sdl3.GamepadType.NintendoSwitchJoyConRight or Sdl3.GamepadType.NintendoSwitchJoyConPair => PadFamily.Nintendo,
        _ => PadFamily.Generic,
    };

    // The PadButton bits a pad lacks, from the SDL_GamepadButton bits its mapping defines and whether
    // it defines each trigger axis. The triggers have no SDL button and come from their axes.
    internal static uint Lacking(uint sdlButtons, bool leftTrigger, bool rightTrigger)
    {
        uint lacking = 0;
        foreach ((PadButton button, Sdl3.GamepadButton sdl) in Mappings)
        {
            if ((sdlButtons & (1u << (int)sdl)) == 0)
            {
                lacking |= 1u << (int)button;
            }
        }

        lacking |= leftTrigger ? 0 : 1u << (int)PadButton.LeftTrigger;
        return lacking | (rightTrigger ? 0 : 1u << (int)PadButton.RightTrigger);
    }

    // snapshot with the active pad's family, the buttons it has, held buttons and those that went down
    // since the last sample, and its axes set through filter. With no pad connected or the window
    // inactive it gains only the last active pad's family and buttons.
    internal DeviceSnapshot SampleOnto(in DeviceSnapshot snapshot, PadFilter filter, bool windowActive, InputEvents events)
    {
        if (events.PadsChanged)
        {
            Reconcile();
        }

        if (events.PadsRemapped)
        {
            _pads.Refresh(_remapPad);
        }

        if (windowActive)
        {
            _pads.Use(events.LastPadPressed);
        }

        bool connected = _pads.TryGetActive(out Pad active);
        if (connected)
        {
            _family = active.Family;
            _lacking = active.Lacking;
        }

        DeviceSnapshot sampled = snapshot.WithPadFamily(_family).WithPadLackingBits(_lacking);
        if (!windowActive || !connected)
        {
            return sampled;
        }

        nint pad = active.Handle;
        sampled = WithPresses(sampled, events.PadButtonsPressed(active.Id));

        foreach ((PadButton button, Sdl3.GamepadButton sdl) in Mappings)
        {
            if (Sdl3.SDL_GetGamepadButton(pad, sdl))
            {
                sampled = sampled.With(button);
            }
        }

        (float leftX, float leftY) = Stick(Sdl3.SDL_GetGamepadAxis(pad, Sdl3.GamepadAxis.LeftX), Sdl3.SDL_GetGamepadAxis(pad, Sdl3.GamepadAxis.LeftY));
        (float rightX, float rightY) = Stick(Sdl3.SDL_GetGamepadAxis(pad, Sdl3.GamepadAxis.RightX), Sdl3.SDL_GetGamepadAxis(pad, Sdl3.GamepadAxis.RightY));
        (leftX, leftY) = filter.Stick(leftX, leftY);
        (rightX, rightY) = filter.Stick(rightX, rightY);

        float leftPull = filter.Trigger(Trigger(Sdl3.SDL_GetGamepadAxis(pad, Sdl3.GamepadAxis.LeftTrigger)));
        float rightPull = filter.Trigger(Trigger(Sdl3.SDL_GetGamepadAxis(pad, Sdl3.GamepadAxis.RightTrigger)));

        // The deadzone is the press threshold. Any pull the filter did not zero is a press.
        if (leftPull > 0f)
        {
            sampled = sampled.With(PadButton.LeftTrigger);
        }

        if (rightPull > 0f)
        {
            sampled = sampled.With(PadButton.RightTrigger);
        }

        return sampled
            .WithAxis(PadAxis.LeftStickX, leftX)
            .WithAxis(PadAxis.LeftStickY, leftY)
            .WithAxis(PadAxis.RightStickX, rightX)
            .WithAxis(PadAxis.RightStickY, rightY)
            .WithAxis(PadAxis.LeftTrigger, leftPull)
            .WithAxis(PadAxis.RightTrigger, rightPull);
    }

    public void Dispose()
    {
        if (!_open)
        {
            return;
        }

        _pads.Clear(static pad => Sdl3.SDL_CloseGamepad(pad.Handle));
        Sdl3.SDL_QuitSubSystem(Sdl3.InitGamepad);
    }

    // Brings the open pads in line with SDL's list. A failed read of the list changes nothing. A
    // joystick the embedded database maps is a gamepad in the list by the time it is read.
    private unsafe void Reconcile()
    {
        if (!_open)
        {
            return;
        }

        if (!_databaseRead && AnyJoystickUnmapped())
        {
            _databaseRead = true;
            GamepadMappings.FillGaps();
        }

        int count;
        uint* ids = Sdl3.SDL_GetGamepads(&count);
        if (ids == null)
        {
            return;
        }

        try
        {
            _pads.Sync(new ReadOnlySpan<uint>(ids, count), _openPad, _closePad);
        }
        finally
        {
            Sdl3.SDL_free((nint)ids);
        }

        ReportUnmapped();
    }

    private static unsafe bool AnyJoystickUnmapped()
    {
        int count;
        uint* ids = Sdl3.SDL_GetJoysticks(&count);
        if (ids == null)
        {
            return false;
        }

        try
        {
            foreach (uint id in new ReadOnlySpan<uint>(ids, count))
            {
                if (!Sdl3.SDL_IsGamepad(id))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Sdl3.SDL_free((nint)ids);
        }
    }

    // Names, once each, every connected joystick SDL has no gamepad mapping for. A player reading the
    // log learns the GUID a mapping line needs.
    private unsafe void ReportUnmapped()
    {
        int count;
        uint* ids = Sdl3.SDL_GetJoysticks(&count);
        if (ids == null)
        {
            return;
        }

        try
        {
            byte* guid = stackalloc byte[33];
            foreach (uint id in new ReadOnlySpan<uint>(ids, count))
            {
                if (Sdl3.SDL_IsGamepad(id) || !_unmapped.Add(id))
                {
                    continue;
                }

                Sdl3.SDL_GUIDToString(Sdl3.SDL_GetJoystickGUIDForID(id), guid, 33);
                string name = Marshal.PtrToStringUTF8((nint)Sdl3.SDL_GetJoystickNameForID(id)) ?? "unnamed";
                Log.Info($"joystick '{name}' ({Marshal.PtrToStringUTF8((nint)guid)}) is not a gamepad. A mapping line for its GUID in {GamepadMappings.PlayerFile} beside the game makes it one");
            }
        }
        finally
        {
            Sdl3.SDL_free((nint)ids);
        }
    }

    private static unsafe Pad? Open(uint id)
    {
        nint handle = Sdl3.SDL_OpenGamepad(id);
        if (handle == nint.Zero)
        {
            Log.Warning($"A connected gamepad could not be opened and will not be read: {Sdl3.Error()}");
            return null;
        }

        string name = Marshal.PtrToStringUTF8((nint)Sdl3.SDL_GetGamepadName(handle)) ?? "unnamed";
        Log.Info($"gamepad '{name}' connected");

        return new Pad(id, handle, name, Family(Sdl3.SDL_GetGamepadType(handle)), Lacking(handle));
    }

    // A remapping can change the pad's type as well as the buttons its mapping defines.
    private static Pad Remap(Pad pad) =>
        pad with { Family = Family(Sdl3.SDL_GetGamepadType(pad.Handle)), Lacking = Lacking(pad.Handle) };

    // What the pad's mapping defines, which SDL answers without reading the hardware.
    private static uint Lacking(nint handle)
    {
        uint buttons = 0;
        foreach ((PadButton _, Sdl3.GamepadButton sdl) in Mappings)
        {
            buttons |= Sdl3.SDL_GamepadHasButton(handle, sdl) ? 1u << (int)sdl : 0;
        }

        return Lacking(
            buttons,
            Sdl3.SDL_GamepadHasAxis(handle, Sdl3.GamepadAxis.LeftTrigger),
            Sdl3.SDL_GamepadHasAxis(handle, Sdl3.GamepadAxis.RightTrigger));
    }

    private static void Close(Pad pad)
    {
        Log.Info($"gamepad '{pad.Name}' disconnected");
        Sdl3.SDL_CloseGamepad(pad.Handle);
    }

    // Buttons with no SDL button are left out of the lookup.
    private static (PadButton, Sdl3.GamepadButton)[] BuildLookup()
    {
        List<(PadButton, Sdl3.GamepadButton)> mappings = [];
        foreach (PadButton button in Enum.GetValues<PadButton>())
        {
            if (ToSdl(button) is { } sdl)
            {
                mappings.Add((button, sdl));
            }
        }

        return [.. mappings];
    }

    // No discard arm: adding a PadButton without a mapping must fail the build (CS8509).
#pragma warning disable CS8524 // PadButton has no unnamed values; only a cast can produce one.
    private static Sdl3.GamepadButton? ToSdl(PadButton button) => button switch
    {
        // None is not a snapshot member, and the triggers derive from the filtered pull.
        PadButton.None => null,
        PadButton.LeftTrigger => null,
        PadButton.RightTrigger => null,

        PadButton.DPadUp => Sdl3.GamepadButton.DPadUp,
        PadButton.DPadDown => Sdl3.GamepadButton.DPadDown,
        PadButton.DPadLeft => Sdl3.GamepadButton.DPadLeft,
        PadButton.DPadRight => Sdl3.GamepadButton.DPadRight,

        PadButton.South => Sdl3.GamepadButton.South,
        PadButton.East => Sdl3.GamepadButton.East,
        PadButton.West => Sdl3.GamepadButton.West,
        PadButton.North => Sdl3.GamepadButton.North,

        PadButton.LeftShoulder => Sdl3.GamepadButton.LeftShoulder,
        PadButton.RightShoulder => Sdl3.GamepadButton.RightShoulder,

        PadButton.LeftStickClick => Sdl3.GamepadButton.LeftStick,
        PadButton.RightStickClick => Sdl3.GamepadButton.RightStick,

        PadButton.Start => Sdl3.GamepadButton.Start,
        PadButton.Select => Sdl3.GamepadButton.Back,
    };
#pragma warning restore CS8524
}
