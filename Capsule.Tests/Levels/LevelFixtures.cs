namespace Capsule.Tests.Levels;

/// <summary>
/// The checked-in Tiled fixtures, and a scratch copy of them. The importer and the CLI both
/// read from disk and resolve paths relative to their inputs, so a spec that mutates anything
/// works on a copy rather than on the fixtures themselves.
/// </summary>
internal static class LevelFixtures
{
    internal static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Levels", "Fixtures", name);

    internal static string Read(string name) => File.ReadAllText(Path(name));

    internal static Workspace CopyRoom()
    {
        Workspace workspace = new();
        foreach (string name in new[] { "room.tmj", "tiles.tsj", "room.level.json" })
        {
            File.Copy(Path(name), workspace.Path(name));
        }

        return workspace;
    }

    internal sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-levels-");

        internal string Path(string name) => System.IO.Path.Combine(_directory.FullName, name);

        internal string Write(string name, string text)
        {
            string path = Path(name);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
