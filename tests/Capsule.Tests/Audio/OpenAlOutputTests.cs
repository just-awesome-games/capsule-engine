using System.Runtime.InteropServices;
using Capsule.Runtime.Desktop.Audio;

namespace Capsule.Tests.Audio;

// The real library on its file-writing driver, configured to report headphones the way a Bluetooth or
// wired headset reports itself to the system. The library reads that configuration once, when it
// loads. This is the only spec in the process that may open it.
public sealed class OpenAlOutputTests
{
    private const string LibraryName = "openal";
    private const int AlcHrtf = 0x1992;

    [Fact]
    public void AHeadphoneOutputPlaysPlainStereoAfterAttachingAndAfterAReopen()
    {
        using TempWorkspace workspace = new("openal-output");
        string config = workspace.PathTo("alsoft.ini");
        File.WriteAllText(config, $"[general]\ndrivers = wave\nstereo-mode = headphones\n[wave]\nfile = {workspace.PathTo("out.wav")}\n");
        SetNativeEnvironment("ALSOFT_CONF", config);

        nint device = alcOpenDevice(null);
        nint context = alcCreateContext(device, nint.Zero);
        alcMakeContextCurrent(context);
        try
        {
            Assert.True(
                HeadphoneFilter(device),
                "The library did not detect the configured headphones. Another spec loaded it first.");

            using OpenAlOutput? output = OpenAlOutput.TryAttach(() => { });
            Assert.NotNull(output);
            Assert.False(HeadphoneFilter(device));

            Assert.True(output.TryReopen(out string? reason), reason);
            Assert.False(HeadphoneFilter(device));
        }
        finally
        {
            alcMakeContextCurrent(nint.Zero);
            alcDestroyContext(context);
            alcCloseDevice(device);
        }
    }

    private static bool HeadphoneFilter(nint device)
    {
        alcGetIntegerv(device, AlcHrtf, 1, out int enabled);

        return enabled != 0;
    }

    // The runtime's own environment block is not the C library's on Linux and macOS, and the library
    // reads the latter.
    private static void SetNativeEnvironment(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(0, setenv(name, value, 1));
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
    private static extern int setenv([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string value, int overwrite);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint alcOpenDevice([MarshalAs(UnmanagedType.LPStr)] string? name);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint alcCreateContext(nint device, nint attributes);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern byte alcMakeContextCurrent(nint context);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void alcDestroyContext(nint context);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern byte alcCloseDevice(nint device);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void alcGetIntegerv(nint device, int parameter, int size, out int value);
#pragma warning restore SYSLIB1054
}
