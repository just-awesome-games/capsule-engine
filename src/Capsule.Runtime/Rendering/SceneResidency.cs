using Capsule.Assets;

namespace Capsule.Runtime.Rendering;

// Loads load and releases release, two disjoint lists valid
// only for the duration of the call.
// scene is the one the new set belongs to, for the wiring fault a stray draw raises; load is
// already located and none of it resident; every handle in release is resident.
internal delegate void TextureSetChanged(
    string scene,
    IReadOnlyList<TextureHandle> load,
    IReadOnlyList<TextureHandle> release);

// Which textures a scene keeps on the device. A set replaces the last one synchronously at the
// transition into its scene: what the new set adds is loaded, what it drops is released, and the
// intersection is never touched. Nothing is reference counted — a handle is resident because the
// current scene's set names it, and for no other reason.
// Device-free: the decode and the dispose are the delegate's.
internal sealed class SceneResidency(TextureSetChanged apply)
{
    private readonly HashSet<TextureHandle> _resident = [];
    private readonly HashSet<TextureHandle> _wanted = [];
    private readonly List<TextureHandle> _load = [];
    private readonly List<TextureHandle> _release = [];

    // What a draw naming a handle the current scene's set does not hold is told.
    internal static string NotResident(string scene, in TextureHandle handle) =>
        $"Scene '{scene}' draws texture '{handle.Name}', which its resident set does not hold. A scene keeps the "
        + "textures its document names and the groups the build derived from the code its spawn types reach; a scene "
        + "drawing anything else declares its own set. The build ships a texture at "
        + $"'{TextureFiles.RelativePathOf(handle)}' only when its source sits under asset-sources/textures.";

    // Makes exactly set resident, in one synchronous pass.
    // Throws FileNotFoundException: A handle the set adds ships no file; nothing changed.
    internal void MakeResident(string scene, IReadOnlyList<TextureHandle> set)
    {
        _wanted.Clear();
        _load.Clear();
        _release.Clear();

        // A set is a union of groups, so one handle may appear in it more than once.
        foreach (TextureHandle handle in set)
        {
            if (_wanted.Add(handle) && !_resident.Contains(handle))
            {
                _load.Add(handle);
            }
        }

        foreach (TextureHandle handle in _resident)
        {
            if (!_wanted.Contains(handle))
            {
                _release.Add(handle);
            }
        }

        // Two scenes over one set is the common transition and both lists are empty then; the call
        // still goes through, so the store learns whose set it holds and a stray draw names the
        // right scene. Recorded only once the device has done the work: a decode that throws leaves
        // the last scene's set both resident and accounted for.
        apply(scene, _load, _release);

        foreach (TextureHandle handle in _release)
        {
            _resident.Remove(handle);
        }

        foreach (TextureHandle handle in _load)
        {
            _resident.Add(handle);
        }
    }
}
