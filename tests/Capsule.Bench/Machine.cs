using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Capsule.Runtime;
using Microsoft.Win32;

namespace Capsule.Bench;

// What a record says about where and what it measured; "unknown" where the machine will not say.
internal static class Machine
{
#if DEBUG
    internal const string Configuration = "Debug";
#else
    internal const string Configuration = "Release";
#endif

    internal static string Os => RuntimeInformation.OSDescription;

    internal static string Commit(string repositoryDirectory)
    {
        try
        {
            ProcessStartInfo start = new("git") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = repositoryDirectory };
            start.ArgumentList.Add("rev-parse");
            start.ArgumentList.Add("--short");
            start.ArgumentList.Add("HEAD");

            using Process git = Process.Start(start)!;
            string output = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();

            return git.ExitCode == 0 && output.Length > 0 ? output : "unknown";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "unknown";
        }
    }

    internal static string Cpu() =>
        RegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Trim() ?? "unknown";

    // MonoGame's adapter is out of every consumer's reach, this shell included, so the first
    // display-class device stands in for it.
    internal static string Gpu() =>
        RegistryString(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", "DriverDesc") ?? "unknown";

    internal static string EngineVersion() =>
        typeof(EngineBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static string? RegistryString(string key, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using RegistryKey? opened = Registry.LocalMachine.OpenSubKey(key);

        return opened?.GetValue(value) as string;
    }
}
