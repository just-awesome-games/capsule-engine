namespace Capsule.Runtime.Assets;

// Owns the assets loaded for one scene. Preloads are exchanged transactionally at a scene
// boundary; an unlisted asset joins the current scene on first use.
internal sealed class SceneAssetStore<THandle, TAsset>(Func<THandle, TAsset> loader) : IDisposable
    where THandle : notnull
    where TAsset : class, IDisposable
{
    private readonly Dictionary<THandle, TAsset> _loaded = [];

    // A hit performs no allocation or I/O. A miss loads once and becomes owned by this scene.
    internal TAsset Get(in THandle handle)
    {
        if (_loaded.TryGetValue(handle, out TAsset? asset))
        {
            return asset;
        }

        asset = loader(handle);

        try
        {
            _loaded.Add(handle, asset);
        }
        catch
        {
            asset.Dispose();
            throw;
        }

        return asset;
    }

    // Makes preloads the next scene's initial ownership. Missing assets load before anything from
    // the current scene is released, so a failed preload leaves the current scene intact.
    internal void ChangeScene(IReadOnlyList<THandle> preloads, Action? prepareRemainingAssets = null)
    {
        HashSet<THandle> wanted = new(preloads.Count);
        List<(THandle Handle, TAsset Asset)> staged = new(preloads.Count);

        try
        {
            foreach (THandle handle in preloads)
            {
                if (wanted.Add(handle) && !_loaded.ContainsKey(handle))
                {
                    staged.Add((handle, loader(handle)));
                }
            }

            // All stores finish loading before any outgoing asset is released. If another
            // store fails, this store rolls back its staged assets too.
            prepareRemainingAssets?.Invoke();
        }
        catch
        {
            Dispose(staged);
            throw;
        }

        foreach ((THandle handle, TAsset asset) in _loaded.ToArray())
        {
            if (!wanted.Contains(handle))
            {
                _loaded.Remove(handle);
                asset.Dispose();
            }
        }

        _loaded.EnsureCapacity(_loaded.Count + staged.Count);
        foreach ((THandle handle, TAsset asset) in staged)
        {
            _loaded.Add(handle, asset);
        }
    }

    public void Dispose()
    {
        foreach (TAsset asset in _loaded.Values)
        {
            asset.Dispose();
        }

        _loaded.Clear();
    }

    private static void Dispose(List<(THandle Handle, TAsset Asset)> staged)
    {
        foreach ((THandle _, TAsset asset) in staged)
        {
            asset.Dispose();
        }
    }
}
