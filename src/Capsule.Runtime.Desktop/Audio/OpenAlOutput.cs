using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Capsule.Runtime.Audio;

namespace Capsule.Runtime.Desktop.Audio;

// The OpenAL device the backend opened, reached through the context it left current, and the two
// OpenAL Soft extensions that let sound follow the operating system's default output: system events,
// which announce that the default moved, and reopen, which moves the device's sources and buffers to
// the new default with everything still playing. When either extension is missing nothing attaches and
// sound stays on the output the run opened.
internal sealed class OpenAlOutput : AudioOutput
{
    private const string LibraryName = "openal";

    private const int AlcConnected = 0x313;
    private const int AlcAllDevicesSpecifier = 0x1013;
    private const int EventDefaultDeviceChanged = 0x19D6;
    private const int PlaybackDevice = 0x19D4;
    private const byte AlcTrue = 1;
    private const byte AlcFalse = 0;

    private readonly nint _device;
    private readonly ReopenDevice _reopen;
    private readonly EventControl _control;
    private readonly EventCallbackControl _subscribe;

    // Rooted for this output's lifetime, because the library holds a thunk the collector cannot see.
    private readonly EventCallback _callback;
    private readonly Action _defaultChanged;

    private OpenAlOutput(
        nint device,
        ReopenDevice reopen,
        EventControl control,
        EventCallbackControl subscribe,
        Action defaultChanged)
    {
        _device = device;
        _reopen = reopen;
        _control = control;
        _subscribe = subscribe;
        _defaultChanged = defaultChanged;
        _callback = OnEvent;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EventCallback(int eventType, int deviceType, nint device, int length, nint message, nint userParam);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte EventControl(int count, in int events, byte enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EventCallbackControl(nint callback, nint userParam);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ReopenDevice(nint device, nint deviceName, nint attribs);

    public override bool Connected
    {
        get
        {
            alcGetIntegerv(_device, AlcConnected, 1, out int connected);

            return connected != 0;
        }
    }

    public override string Name => Marshal.PtrToStringUTF8(alcGetString(_device, AlcAllDevicesSpecifier)) ?? "";

    // Subscribes defaultChanged to the system's default playback device changing, or returns null when
    // the device or the extensions are absent. defaultChanged runs on one of the library's own threads,
    // possibly at real-time priority with a small stack, so it may only note the change. Call once,
    // after the backend has opened its device.
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
            Marshal.GetDelegateForFunctionPointer<EventCallbackControl>(subscribe),
            defaultChanged);

        output._subscribe(Marshal.GetFunctionPointerForDelegate(output._callback), nint.Zero);

        if (output._control(1, EventDefaultDeviceChanged, AlcTrue) != AlcTrue)
        {
            output.Dispose();

            return null;
        }

        return output;
    }

    // On failure the library's error for the device is reported and cleared.
    public override bool TryReopen([NotNullWhen(false)] out string? reason)
    {
        if (_reopen(_device, nint.Zero, nint.Zero) == AlcTrue)
        {
            reason = null;

            return true;
        }

        reason = $"OpenAL error 0x{alcGetError(_device):X}";

        return false;
    }

    public override void Dispose()
    {
        _control(1, EventDefaultDeviceChanged, AlcFalse);
        _subscribe(nint.Zero, nint.Zero);
    }

    private void OnEvent(int eventType, int deviceType, nint device, int length, nint message, nint userParam)
    {
        if (eventType == EventDefaultDeviceChanged && deviceType == PlaybackDevice)
        {
            _defaultChanged();
        }
    }

    // LibraryImport generates unsafe marshalling stubs, and these calls do not justify opening the
    // assembly to unsafe code. Handles are already pointers, and the strings are ASCII names.
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
