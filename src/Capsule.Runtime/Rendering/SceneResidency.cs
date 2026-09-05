using Capsule.Assets;

namespace Capsule.Runtime.Rendering;

/// <summary>
/// Loads <paramref name="load"/> and releases <paramref name="release"/>, two disjoint lists valid
/// only for the duration of the call.
/// </summary>
/// <param name="scene">The scene the new set belongs to, for the wiring fault a stray draw raises.</param>
/// <param name="load">Handles the new set adds; already located, none of them resident.</param>
/// <param name="release">Handles the new set drops; every one of them resident.</param>
internal delegate void TextureSetChanged(
    string scene,
    IReadOnlyList<TextureHandle> load,
    IReadOnlyList<TextureHandle> release);

/// <summary>
/// Which textures a scene keeps on the device. A set replaces the last one synchronously at the
/// transition into its scene: what the new set adds is loaded, what it drops is released, and the
/// intersection is never touched. Nothing is reference counted — a handle is resident because the
/// current scene's set names it, and for no other reason.
/// </summary>
/// <remarks>Device-free: the decode and the dispose are the delegate's.</remarks>
internal sealed class SceneResidency(TextureSetChanged apply)
{
    private readonly HashSet<TextureHandle> _resident = [];
    private readonly HashSet<TextureHandle> _wanted = [];
    private readonly List<TextureHandle> _load = [];
    private readonly List<TextureHandle> _release = [];

    /// <summary>What a draw naming a handle the current scene's set does not hold is told.</summary>
    internal static string NotResident(string scene, in TextureHandle handle) =>
        $"Scene '{scene}' draws texture '{handle.Name}', which its resident set does not hold. A scene keeps the "
        + "textures its document names and the groups the build derived from the code its spawn types reach; a scene "
        + "drawing anything else declares its own set. The build ships a texture at "
        + $"'{TextureFiles.RelativePathOf(handle)}' only when its source sits under asset-sources/textures.";

    /// <summary>Makes exactly <paramref name="set"/> resident, in one synchronous pass.</summary>
    /// <exception cref="FileNotFoundException">A handle the set adds ships no file; nothing changed.</exception>
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

        // Two scenes over one set is the common transition, and the device has no work in it.
        if (_load.Count == 0 && _release.Count == 0)
        {
            return;
        }

        // Recorded only once the device has done the work: a decode that throws leaves the last
        // scene's set both resident and accounted for.
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
