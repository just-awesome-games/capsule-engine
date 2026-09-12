using System.Runtime.InteropServices;

namespace Capsule.Runtime.Audio;

// The OpenAL device the backend opened, reached through the context it left current, and the two
// OpenAL Soft extensions that let sound follow the operating system's default output: system events,
// which announce that the default moved, and reopen, which moves the device's sources and buffers
// to the new default with everything still playing. Attaches to nothing when either is missing, in
// which case sound stays on the output the run opened.
internal sealed class OpenAlOutput : IDisposable
{
    private const string LibraryName = "openal";

    private const int AlcConnected = 0x313;
    private const int AlcAllDevicesSpecifier = 0x1013;
    private const int EventDefaultDeviceChanged = 0x19D6;
    private const int PlaybackDevice = 0x19D4;
    private const byte AlcTrue = 1;
    private const byte AlcFalse = 0;

    // Rooted for the device's lifetime: the library holds the thunk, which the collector cannot see.
    // Static because the backend opens one device per process, and the event carries no handle to
    // route on.
    private static EventCallback? Callback;
    private static Action? DefaultChanged;

    private readonly nint _device;
    private readonly ReopenDevice _reopen;
    private readonly EventControl _control;
    private readonly EventCallbackControl _subscribe;

    private OpenAlOutput(nint device, ReopenDevice reopen, EventControl control, EventCallbackControl subscribe)
    {
        _device = device;
        _reopen = reopen;
        _control = control;
        _subscribe = subscribe;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EventCallback(int eventType, int deviceType, nint device, int length, nint message, nint userParam);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte EventControl(int count, in int events, byte enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EventCallbackControl(nint callback, nint userParam);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ReopenDevice(nint device, nint deviceName, nint attribs);

    // Whether the output is still attached to a system device. A device the system pulled — a
    // headset switched off — plays into nothing until reopened.
    internal bool Connected
    {
        get
        {
            alcGetIntegerv(_device, AlcConnected, 1, out int connected);

            return connected != 0;
        }
    }

    // The system's name for the output the device is on.
    internal string Name => Marshal.PtrToStringUTF8(alcGetString(_device, AlcAllDevicesSpecifier)) ?? "";

    // Subscribes defaultChanged to the system's default playback device changing, or answers null
    // when the device or the extensions are not there. defaultChanged runs on a thread of the
    // library's own, possibly at real-time priority with a small stack: it may note the change and
    // nothing else. Call once, after the backend has opened its device.
    internal static OpenAlOutput? TryAttach(Action defaultChanged)
    {
        nint context = alcGetCurrentContext();
        nint device = context == nint.Zero ? nint.Zero : alcGetContextsDevice(context);
        if (device == nint.Zero
            || alcIsExtensionPresent(device, "ALC_SOFT_system_events") != AlcTrue
            || alcIsExtensionPresent(device, "ALC_SOFT_reopen_device") != AlcTrue)
        {
            return null;
        }

        nint reopen = alcGetProcAddress(device, "alcReopenDeviceSOFT");
        nint control = alcGetProcAddress(device, "alcEventControlSOFT");
        nint subscribe = alcGetProcAddress(device, "alcEventCallbackSOFT");
        if (reopen == nint.Zero || control == nint.Zero || subscribe == nint.Zero)
        {
            return null;
        }

        OpenAlOutput output = new(
            device,
            Marshal.GetDelegateForFunctionPointer<ReopenDevice>(reopen),
            Marshal.GetDelegateForFunctionPointer<EventControl>(control),
            Marshal.GetDelegateForFunctionPointer<EventCallbackControl>(subscribe));

        DefaultChanged = defaultChanged;
        Callback = OnEvent;
        output._subscribe(Marshal.GetFunctionPointerForDelegate(Callback), nint.Zero);

        if (output._control(1, EventDefaultDeviceChanged, AlcTrue) != AlcTrue)
        {
            output.Dispose();

            return null;
        }

        return output;
    }

    // Moves the device to whatever the system's default output now is, keeping every source and
    // buffer, and restores a disconnected device to a connected one. On failure the device stays
    // where it was and the library's error for it is reported and cleared.
    internal bool TryReopen(out int error)
    {
        if (_reopen(_device, nint.Zero, nint.Zero) == AlcTrue)
        {
            error = 0;

            return true;
        }

        error = alcGetError(_device);

        return false;
    }

    public void Dispose()
    {
        _control(1, EventDefaultDeviceChanged, AlcFalse);
        _subscribe(nint.Zero, nint.Zero);
        Callback = null;
        DefaultChanged = null;
    }

    private static void OnEvent(int eventType, int deviceType, nint device, int length, nint message, nint userParam)
    {
        if (eventType == EventDefaultDeviceChanged && deviceType == PlaybackDevice)
        {
            DefaultChanged?.Invoke();
        }
    }

    // LibraryImport generates unsafe marshalling stubs, and these do not justify opening the whole
    // assembly to unsafe code: handles are pointers already, and the strings are ASCII names.
#pragma warning disable SYSLIB1054
    [DllImport(LibraryName, EntryPoint = "alcGetCurrentContext", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint alcGetCurrentContext();

    [DllImport(LibraryName, EntryPoint = "alcGetContextsDevice", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint alcGetContextsDevice(nint context);

    [DllImport(LibraryName, EntryPoint = "alcIsExtensionPresent", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte alcIsExtensionPresent(nint device, [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(LibraryName, EntryPoint = "alcGetProcAddress", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint alcGetProcAddress(nint device, [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(LibraryName, EntryPoint = "alcGetIntegerv", CallingConvention = CallingConvention.Cdecl)]
    private static extern void alcGetIntegerv(nint device, int parameter, int size, out int value);

    [DllImport(LibraryName, EntryPoint = "alcGetError", CallingConvention = CallingConvention.Cdecl)]
    private static extern int alcGetError(nint device);

    [DllImport(LibraryName, EntryPoint = "alcGetString", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint alcGetString(nint device, int parameter);
#pragma warning restore SYSLIB1054
}
