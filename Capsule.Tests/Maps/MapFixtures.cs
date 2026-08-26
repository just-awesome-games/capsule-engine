namespace Capsule.Tests.Maps;

/// <summary>
/// The checked-in Tiled fixtures, and a scratch copy of them. The importer and the CLI both
/// read from disk and resolve paths relative to their inputs, so a spec that mutates anything
/// works on a copy rather than on the fixtures themselves.
/// </summary>
internal static class MapFixtures
{
    internal static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Maps", "Fixtures", name);

    internal static string Read(string name) => File.ReadAllText(Path(name));

    /// <summary>
    /// A scratch tree holding the room fixture once per name — <c>"room"</c>, <c>"a/room"</c> —
    /// each as <c>&lt;name&gt;.tmj</c> beside the tileset it references.
    /// </summary>
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

    /// <summary>
    /// A scratch tree that is also the working directory for as long as it lives. The importer
    /// stamps a map path as it received it, so a spec only means anything if it drives the tool
    /// the way the build does — from the directory the map paths are written against. Every
    /// name a workspace takes or hands back is therefore relative.
    /// </summary>
    internal sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-maps-");
        private readonly string _entryDirectory = Directory.GetCurrentDirectory();

        internal Workspace() => Directory.SetCurrentDirectory(_directory.FullName);

        internal string Write(string name, string text)
        {
            File.WriteAllText(System.IO.Path.Combine(_directory.FullName, name), text);
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
