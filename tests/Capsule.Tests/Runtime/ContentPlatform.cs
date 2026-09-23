using Capsule.Persistence;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

// A platform whose content is a directory the spec shipped into; every other member is the
// no-window default, and a save storage is never asked for.
internal sealed class ContentPlatform(string root) : HostPlatform
{
    protected internal override Stream OpenContent(string relativePath) => File.OpenRead(Path.Combine(root, relativePath));

    protected internal override ISaveStorage OpenSaveStorage(string localFolderName) => throw new NotSupportedException();
}
