using System.Runtime.InteropServices;
using Capsule.Runtime.Desktop;

namespace Capsule.Tests.Runtime;

// The desktop platform's reads of the SDL3 it ships, made against the real library and, for events,
// through the sdl2-compat bridge the graphics backend runs on.
[Collection(SdlEventsCollection.Name)]
public sealed class SdlPlatformTests
{
    private const uint BridgeEventsSubsystem = 0x00004000;
    private const uint WindowExposed = 0x204;
    private const uint WindowMoved = 0x205;
    private const int EventSize = 128;
    private const int WindowIdOffset = 16;

    private static readonly string BridgeFile =
        OperatingSystem.IsWindows() ? "SDL2.dll"
        : OperatingSystem.IsMacOS() ? "libSDL2-2.0.0.dylib"
        : "libSDL2-2.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int BridgeInitSubSystem(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BridgeQuitSubSystem(uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BridgeSetEventFilter(nint filter, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte PushEvent(nint sdlEvent);

    // SDL3 answers with an array of pointers to locales. Read as an array of locales, the tags come
    // back as pointer bytes or the read faults.
    [Fact]
    public void PreferredLocales_AreLanguageTags_ReadFromSdl3()
    {
        IReadOnlyList<string> tags = SdlPlatform.PreferredLocales();

        Assert.All(tags, tag => Assert.Matches("^[A-Za-z]{1,8}(-[A-Za-z0-9]{1,8})?$", tag));
    }

    // The bridge's filter consumes SDL3's own window events and posts each again in SDL2's shape, and a
    // watch matching SDL3's types hears nothing. A size change needs a real window and is not pushed.
    [Fact]
    public void RedrawWatch_HearsAnExposeThroughTheBridge_AndNotAMove()
    {
        SdlPlatform.Load();
        nint bridge = NativeLibrary.Load(BridgeFile, typeof(SdlPlatform).Assembly, null);
        BridgeInitSubSystem init = Export<BridgeInitSubSystem>(bridge, "SDL_InitSubSystem");
        BridgeQuitSubSystem quit = Export<BridgeQuitSubSystem>(bridge, "SDL_QuitSubSystem");
        PushEvent push = Export<PushEvent>(SdlPlatform.Library, "SDL_PushEvent");

        Assert.Equal(0, init(BridgeEventsSubsystem));
        nint sdlEvent = Marshal.AllocHGlobal(EventSize);
        try
        {
            // The backend installs the bridge's filter as it creates its window. Setting no SDL2 filter
            // installs the bridge's alone.
            Export<BridgeSetEventFilter>(bridge, "SDL_SetEventFilter")(nint.Zero, nint.Zero);

            int redraws = 0;
            using (new SdlPlatform.RedrawWatch(() => redraws++))
            {
                Push(push, sdlEvent, WindowExposed);
                Push(push, sdlEvent, WindowMoved);
            }

            Assert.Equal(1, redraws);
        }
        finally
        {
            Marshal.FreeHGlobal(sdlEvent);
            quit(BridgeEventsSubsystem);
        }
    }

    private static T Export<T>(nint library, string name)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    // A zeroed SDL3 window event of the given type for window 1, which SDL stamps as it is pushed.
    private static void Push(PushEvent push, nint sdlEvent, uint type)
    {
        Marshal.Copy(new byte[EventSize], 0, sdlEvent, EventSize);
        Marshal.WriteInt32(sdlEvent, (int)type);
        Marshal.WriteInt32(sdlEvent, WindowIdOffset, 1);
        push(sdlEvent);
    }
}
