using System.ComponentModel;
using Capsule.Assets;

namespace Capsule.Scenes;

/// <summary>
/// Adds the residency groups one scene or one spawned entity reaches to <paramref name="set"/>,
/// which may already carry another's. Repeats are the union's business, not the builder's.
/// </summary>
/// <param name="set">The scene set being assembled; appended to, never cleared.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void TextureSetBuilder(List<TextureHandle> set);
