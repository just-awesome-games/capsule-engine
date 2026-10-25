using System.Reflection;
using System.Runtime.InteropServices;

namespace Capsule.Runtime.Sdl;

// The SDL3 calls the host makes for input and the cursor. The graphics backend keeps its own SDL2
// binding, which sdl2-compat forwards to this same SDL3, and pumps the events. These calls read
// device state, watch events and drive the cursor, and never pump. Every signature, enum value and
// struct layout here is SDL 3.4's.
internal static unsafe class Sdl3
{
    // The one name every binding here imports, which a console shell links statically through
    // DirectPInvoke.
    internal const string LibraryName = "SDL3";

    internal const uint InitEvents = 0x00004000;
    internal const uint InitGamepad = 0x00002000;

    // SDL_EventType.
    internal const uint EventKeyDown = 0x300;
    internal const uint EventKeyUp = 0x301;
    internal const uint EventKeymapChanged = 0x304;
    internal const uint EventMouseButtonDown = 0x401;
    internal const uint EventMouseWheel = 0x403;
    internal const uint EventGamepadAxisMotion = 0x650;
    internal const uint EventGamepadButtonDown = 0x651;
    internal const uint EventGamepadAdded = 0x653;
    internal const uint EventGamepadRemoved = 0x654;
    internal const uint EventJoystickAdded = 0x605;
    internal const uint EventJoystickRemoved = 0x606;

    // SDL_MOUSEWHEEL_FLIPPED: the wheel event's values are reversed, as natural scrolling reports them.
    internal const int MouseWheelFlipped = 1;

    // sizeof(SDL_Event). SDL_PushEvent copies this many bytes from what it is handed.
    internal const int EventSize = 128;

    // SDL_SCANCODE_COUNT, the length of the keyboard state array.
    internal const int ScancodeCount = 512;

    // SDLK_SCANCODE_MASK and SDLK_EXTENDED_MASK. A keycode with either bit set names a key that
    // prints no character.
    internal const uint KeycodeScancodeMask = 1u << 30;
    internal const uint KeycodeExtendedMask = 1u << 29;

    // SDL_KMOD_NONE.
    internal const ushort KeymodNone = 0;

    // SDL_PIXELFORMAT_RGBA32: bytes in R, G, B, A order in memory, which is a packed ABGR8888 on a
    // little-endian machine and RGBA8888 on a big-endian one.
    internal static readonly uint PixelFormatRgba32 = BitConverter.IsLittleEndian ? 0x16762004u : 0x16462004u;

    // On Linux the library ships under its versioned soname, which the default probe for the bare
    // name never derives.
    private const string VersionedName = "libSDL3.so.0";

    private static nint Library;

    static Sdl3() => NativeLibrary.SetDllImportResolver(typeof(Sdl3).Assembly, Resolve);

    // SDL_PROP_GAMEPAD_CAP_RUMBLE_BOOLEAN and SDL_PROP_GAMEPAD_CAP_TRIGGER_RUMBLE_BOOLEAN, null-terminated.
    internal static ReadOnlySpan<byte> GamepadCapRumble => "SDL.joystick.cap.rumble\0"u8;

    internal static ReadOnlySpan<byte> GamepadCapTriggerRumble => "SDL.joystick.cap.trigger_rumble\0"u8;

    // SDL's last error message on this thread.
    internal static string Error() => Marshal.PtrToStringUTF8((nint)SDL_GetError()) ?? string.Empty;

    // SDL_GamepadButton.
    internal enum GamepadButton
    {
        South,
        East,
        West,
        North,
        Back,
        Guide,
        Start,
        LeftStick,
        RightStick,
        LeftShoulder,
        RightShoulder,
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight,
    }

    // SDL_GamepadType.
    internal enum GamepadType
    {
        Unknown,
        Standard,
        Xbox360,
        XboxOne,
        PS3,
        PS4,
        PS5,
        NintendoSwitchPro,
        NintendoSwitchJoyConLeft,
        NintendoSwitchJoyConRight,
        NintendoSwitchJoyConPair,
        GameCube,
        Steam,
    }

    // SDL_GamepadAxis.
    internal enum GamepadAxis
    {
        LeftX,
        LeftY,
        RightX,
        RightY,
        LeftTrigger,
        RightTrigger,
    }

    // The bare name first, which finds the library beside the executable or under the build's
    // runtimes/ folder on every desktop, then the soname.
    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return nint.Zero;
        }

        if (Library == nint.Zero
            && !NativeLibrary.TryLoad(LibraryName, assembly, searchPath, out Library)
            && !NativeLibrary.TryLoad(VersionedName, assembly, searchPath, out Library))
        {
            Library = nint.Zero;
        }

        return Library;
    }

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_InitSubSystem(uint flags);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_QuitSubSystem(uint flags);

    // Nanoseconds since SDL's initialisation, the clock every event's timestamp is on.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern ulong SDL_GetTicksNS();

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetError();

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_free(nint memory);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_AddEventWatch(delegate* unmanaged<nint, nint, byte> filter, nint userData);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_RemoveEventWatch(delegate* unmanaged<nint, nint, byte> filter, nint userData);

    // Runs the watches on the calling thread and queues the event. Pointed at EventSize bytes.
    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_PushEvent(nint sdlEvent);

    // SDL's own array of ScancodeCount one-byte bools, valid for the life of the process.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetKeyboardState(int* count);

    // The SDL_Keycode the active layout puts on scancode under modifiers.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetKeyFromScancode(Scancode scancode, ushort modifiers, [MarshalAs(UnmanagedType.U1)] bool keyEvent);

    // A UTF-8 name for key, valid until the next call. A letter is uppercase.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetKeyName(uint key);

    // The held buttons as SDL_BUTTON_MASK bits, and the pointer in window coordinates.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetMouseState(float* x, float* y);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint* SDL_GetGamepads(int* count);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_OpenGamepad(uint id);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_CloseGamepad(nint gamepad);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_GetGamepadFromID(uint id);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetGamepadName(nint gamepad);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern GamepadType SDL_GetGamepadType(nint gamepad);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetGamepadButton(nint gamepad, GamepadButton button);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern short SDL_GetGamepadAxis(nint gamepad, GamepadAxis axis);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint SDL_GetGamepadProperties(nint gamepad);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_GetBooleanProperty(uint properties, byte* name, [MarshalAs(UnmanagedType.U1)] bool defaultValue);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RumbleGamepad(nint gamepad, ushort low, ushort high, uint milliseconds);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_RumbleGamepadTriggers(nint gamepad, ushort left, ushort right, uint milliseconds);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_ShowCursor();

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_HideCursor();

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_SetCursor(nint cursor);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_GetDefaultCursor();

    // SDL converts and copies the surface's pixels. The surface can go once this returns.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateColorCursor(nint surface, int hotX, int hotY);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DestroyCursor(nint cursor);

    // A surface over the caller's pixels, which it neither copies nor frees.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_CreateSurfaceFrom(int width, int height, uint format, void* pixels, int pitch);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_DestroySurface(nint surface);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_IOFromConstMem(void* memory, nuint size);

    // The mappings added, or -1 on failure. A mapping replaces an earlier one for its GUID of the same
    // or lower priority, and SDL's environment hints hold a higher one than any added here.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern int SDL_AddGamepadMappingsFromIO(nint stream, [MarshalAs(UnmanagedType.U1)] bool closeStream);

    // A mapping SDL's tables hold for guid, which the caller frees, or zero. Drivers' generated mappings
    // are not consulted.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern nint SDL_GetGamepadMappingForGUID(JoystickGuid guid);

    // 1 for a new mapping, 0 for one that replaced an earlier, or -1 on failure.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern int SDL_AddGamepadMapping([MarshalAs(UnmanagedType.LPUTF8Str)] string mapping);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetPlatform();

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern JoystickGuid SDL_StringToGUID([MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern uint* SDL_GetJoysticks(int* count);

    [DllImport(LibraryName, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool SDL_IsGamepad(uint id);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern byte* SDL_GetJoystickNameForID(uint id);

    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern JoystickGuid SDL_GetJoystickGUIDForID(uint id);

    // Writes 32 hex digits and a terminator into a buffer of at least 33 bytes.
    [DllImport(LibraryName, ExactSpelling = true)]
    internal static extern void SDL_GUIDToString(JoystickGuid guid, byte* text, int capacity);

    // SDL_GUID, sixteen bytes passed by value.
    [StructLayout(LayoutKind.Sequential)]
    internal struct JoystickGuid
    {
        public ulong Low;
        public ulong High;
    }

    // The event layouts the input watch reads, field for field from SDL_events.h. Each starts with
    // SDL_CommonEvent's type, reserved word and timestamp.
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardEvent
    {
        public uint Type;
        public uint Reserved;
        public ulong Timestamp;
        public uint WindowId;
        public uint Which;
        public int Scancode;
        public uint Key;
        public ushort Mod;
        public ushort Raw;
        public byte Down;
        public byte Repeat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseButtonEvent
    {
        public uint Type;
        public uint Reserved;
        public ulong Timestamp;
        public uint WindowId;
        public uint Which;
        public byte Button;
        public byte Down;
        public byte Clicks;
        public byte Padding;
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseWheelEvent
    {
        public uint Type;
        public uint Reserved;
        public ulong Timestamp;
        public uint WindowId;
        public uint Which;
        public float X;
        public float Y;
        public int Direction;
        public float MouseX;
        public float MouseY;
        public int IntegerX;
        public int IntegerY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GamepadAxisEvent
    {
        public uint Type;
        public uint Reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Axis;
        public byte Padding1;
        public byte Padding2;
        public byte Padding3;
        public short Value;
        public ushort Padding4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GamepadButtonEvent
    {
        public uint Type;
        public uint Reserved;
        public ulong Timestamp;
        public uint Which;
        public byte Button;
        public byte Down;
        public byte Padding1;
        public byte Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GamepadDeviceEvent
    {
        public uint Type;
        public uint Reserved;
        public ulong Timestamp;
        public uint Which;
    }
}
