namespace Capsule.Tests.Runtime;

// A directory of its own for one spec, with everything written into it removed on Dispose: what a
// spec that ships files, writes a capture or reads back a CSV runs inside.
internal sealed class TempWorkspace(string name) : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory(name);

    internal string Root => _directory.FullName;

    // The full path of a file inside this workspace, its parent directory already created; the
    // relative path may name directories, with either separator.
    internal string PathTo(string relativePath)
    {
        string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        return full;
    }

    public void Dispose() => _directory.Delete(recursive: true);
}
