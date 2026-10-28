using System.Globalization;
using Capsule.Diagnostics;

namespace Capsule.Runtime.DevTools.Recordings;

// Where a run's recordings are written. A saved one goes to the directory and is the developer's. An
// automatic one, at exit or on a crash, goes to Recent, which keeps the newest RecentKept. The engine
// deletes nothing outside Recent.
internal static class RecordingFiles
{
    internal const string DirectoryName = "Recordings";
    internal const string RecentName = "Recent";
    internal const string Extension = ".recording";
    internal const int RecentKept = 20;

    // Returns the file's path. The name is the scene's and the local time's, suffixed on a collision. A
    // character no file name may hold, such as a document key's separator, is written as an underscore.
    internal static string Save(Recorder recorder, string directory, string sceneName, DateTime now, bool crashed = false)
    {
        Directory.CreateDirectory(directory);
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            sceneName = sceneName.Replace(invalid, '_');
        }

        string stem = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{sceneName} {now:yyyy-MM-dd HH-mm-ss}"));
        string path = stem + Extension;
        for (int copy = 2; File.Exists(path); copy++)
        {
            path = string.Create(CultureInfo.InvariantCulture, $"{stem} ({copy}){Extension}");
        }

        using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write))
        {
            recorder.Write(file, crashed);
        }

        return path;
    }

    // An automatic save into Recent, then the prune. Returns the path, or null when the write failed, which
    // is logged and never masks how the run ended.
    internal static string? SaveRecent(Recorder recorder, string directory, string sceneName, bool crashed)
    {
        try
        {
            string recent = Path.Combine(directory, RecentName);
            string path = Save(recorder, recent, sceneName, DateTime.Now, crashed);
            Prune(recent);

            return path;
        }
        catch (Exception failure)
        {
            Log.Warning($"the run's recording could not be saved under {directory}: {failure.Message}");

            return null;
        }
    }

    // Keeps the newest RecentKept recordings in recent by write time.
    internal static void Prune(string recent)
    {
        FileInfo[] files = new DirectoryInfo(recent).GetFiles("*" + Extension);
        if (files.Length <= RecentKept)
        {
            return;
        }

        Array.Sort(files, static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for (int index = RecentKept; index < files.Length; index++)
        {
            files[index].Delete();
        }
    }
}
