namespace Capsule.Tests.Maps;

internal static class MapFixtures
{
    internal static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Maps", "Fixtures", name);

    internal static string Read(string name) => File.ReadAllText(Path(name));

    internal static Workspace CopyMaps(params string[] mapNames)
    {
        Workspace workspace = new();
        foreach (string mapName in mapNames)
        {
            string map = mapName + ".tmj";
            string directory = System.IO.Path.GetDirectoryName(map)!;
            if (directory.Length > 0)
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(Path("room.tmj"), map);

            string tileset = System.IO.Path.Combine(directory, "tiles.tsj");
            if (!File.Exists(tileset))
            {
                File.Copy(Path("tiles.tsj"), tileset);
            }
        }

        return workspace;
    }

    internal sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-maps-");
        private readonly string _entryDirectory = Directory.GetCurrentDirectory();

        internal Workspace() => Directory.SetCurrentDirectory(_directory.FullName);

        internal string Write(string name, string text)
        {
            string path = System.IO.Path.Combine(_directory.FullName, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);

            return name;
        }

        // Out of the tree before deleting it: a working directory cannot be removed on Windows.
        public void Dispose()
        {
            Directory.SetCurrentDirectory(_entryDirectory);
            _directory.Delete(recursive: true);
        }
    }
}
