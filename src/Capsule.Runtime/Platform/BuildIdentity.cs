using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Capsule.Runtime.Platform;

// The running build, as a crash log's header names it: the game's and Capsule's versions, the system,
// and the executable's hash, which matches the log to the archived symbols that decode its frames.
internal static class BuildIdentity
{
    private const string Unknown = "unknown";

    // One line a part. A part that cannot be read says so, and none throws.
    internal static string Describe(string gameName) =>
        string.Join(
            Environment.NewLine,
            $"{gameName} {Version(Assembly.GetEntryAssembly())}",
            $"Capsule {Version(typeof(BuildIdentity).Assembly)}",
            $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}",
            Executable(Environment.ProcessPath));

    // The informational version, which the SDK suffixes with the commit it built from. An assembly
    // without one reads unknown, which the NativeAOT smoke relies on to catch trimming that drops it.
    internal static string Version(Assembly? assembly)
    {
        try
        {
            return assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Unknown;
        }
        catch (Exception)
        {
            return Unknown;
        }
    }

    internal static string Executable(string? path)
    {
        if (path is null)
        {
            return $"{Unknown} sha256 {Unknown}";
        }

        try
        {
            using FileStream file = File.OpenRead(path);
            return $"{Path.GetFileName(path)} sha256 {Convert.ToHexStringLower(SHA256.HashData(file))}";
        }
        catch (Exception)
        {
            return $"{Path.GetFileName(path)} sha256 {Unknown}";
        }
    }
}
