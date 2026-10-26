using System.Runtime.CompilerServices;

namespace Capsule.Runtime.Platform;

// The directory shipped content is read from. A native publish asks SDL, which knows a macOS bundle and
// a console's content mount. Under the JIT the .NET muxer may own the process, as a test host does on
// Linux and macOS, and SDL would answer with the muxer's directory.
internal static class ContentFolder
{
    private static string? Root;

    internal static string Resolve() => Root ??= RuntimeFeature.IsDynamicCodeSupported ? AppContext.BaseDirectory : Sdl3.BasePath();
}
