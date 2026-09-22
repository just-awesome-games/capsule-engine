namespace Capsule.Runtime.Assets;

// A decoded asset waiting for the game thread to create it on the device.
internal interface IPendingAsset<TAsset>
{
    // Spends budget on the device and returns whether the asset is ready to finish.
    bool Advance(ref long budget);

    TAsset Finish();

    // Safe on any thread until Advance has run, because nothing is on the device yet.
    void Discard();
}

// Owns one scene's assets, the next scene's prefetched ones and the decodes in flight, keyed by file.
internal sealed class SceneAssetStore<THandle, TAsset> : IDisposable
    where THandle : notnull
    where TAsset : class, IDisposable
{
    private readonly Func<THandle, IPendingAsset<TAsset>> _decode;
    private readonly long _uploadBudget;

    private readonly Dictionary<THandle, TAsset> _loaded = [];
    private readonly Dictionary<THandle, Decode> _decoding = [];

    // Created ahead of the boundary that wants them, and owned by no scene until it arrives.
    private readonly Dictionary<THandle, TAsset> _warm = [];
    private readonly HashSet<THandle> _prefetch = [];

    private (THandle Handle, IPendingAsset<TAsset> Pending)? _uploading;

    // decode runs on the thread pool. Pump spends uploadBudget a frame on prefetched assets.
    internal SceneAssetStore(Func<THandle, IPendingAsset<TAsset>> decode, long uploadBudget)
    {
        _decode = decode;
        _uploadBudget = uploadBudget;
    }

    // A hit performs no allocation or I/O.
    internal bool TryGet(in THandle handle, out TAsset asset) => _loaded.TryGetValue(handle, out asset!);

    // Makes an asset the current scene did not declare its own.
    internal TAsset Load(in THandle handle)
    {
        TAsset asset = _warm.Remove(handle, out TAsset? warm) ? warm
            : TakeUpload(handle) is { } pending ? pending.Finish()
            : _decoding.Remove(handle, out Decode? decode) ? Result(decode).Finish()
            : _decode(handle).Finish();

        _loaded.Add(handle, asset);

        return asset;
    }

    // Starts the decodes of the prefetch set and releases what the previous set warmed alone.
    internal void Prefetch(IReadOnlyList<THandle> handles)
    {
        _prefetch.Clear();
        foreach (THandle handle in handles)
        {
            if (_prefetch.Add(handle))
            {
                Start(handle);
            }
        }

        foreach ((THandle handle, TAsset asset) in _warm.ToArray())
        {
            if (!_prefetch.Contains(handle))
            {
                _warm.Remove(handle);
                asset.Dispose();
            }
        }

        DiscardOutside(_prefetch);
    }

    // Once a frame on the game thread. A prefetch that fails is dropped, and the boundary that wants it
    // loads it again.
    internal void Pump()
    {
        long budget = _uploadBudget;
        while (budget > 0 && (_uploading is not null || TakeFinishedPrefetch()))
        {
            (THandle handle, IPendingAsset<TAsset> pending) = _uploading!.Value;
            try
            {
                if (!pending.Advance(ref budget))
                {
                    return;
                }

                _uploading = null;
                _warm.Add(handle, pending.Finish());
            }
            catch
            {
                if (_uploading is not null)
                {
                    _uploading = null;
                    pending.Discard();
                }
            }
        }
    }

    // Makes preloads the next scene's initial ownership. Missing assets are created before anything
    // from the current scene is released, and a failed preload leaves the current scene intact.
    internal void ChangeScene(IReadOnlyList<THandle> preloads, Action? prepareRemainingAssets = null)
    {
        HashSet<THandle> wanted = new(preloads.Count);
        foreach (THandle handle in preloads)
        {
            if (wanted.Add(handle))
            {
                Start(handle);
            }
        }

        List<(THandle Handle, TAsset Asset, bool Warm)> staged = new(preloads.Count);

        try
        {
            foreach (THandle handle in wanted)
            {
                if (_loaded.ContainsKey(handle))
                {
                    continue;
                }

                if (_warm.Remove(handle, out TAsset? warm))
                {
                    staged.Add((handle, warm, true));
                }
                else if (TakeUpload(handle) is { } pending)
                {
                    staged.Add((handle, pending.Finish(), false));
                }
                else
                {
                    _decoding.Remove(handle, out Decode? decode);
                    staged.Add((handle, Result(decode!).Finish(), false));
                }
            }

            // Every store finishes loading before any outgoing asset is released. If another store
            // fails, this store rolls back its staged assets too.
            prepareRemainingAssets?.Invoke();
        }
        catch
        {
            DiscardOutside(_prefetch);

            foreach ((THandle handle, TAsset asset, bool isWarm) in staged)
            {
                if (isWarm)
                {
                    _warm.Add(handle, asset);
                }
                else
                {
                    asset.Dispose();
                }
            }

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
        foreach ((THandle handle, TAsset asset, bool _) in staged)
        {
            _loaded.Add(handle, asset);
        }

        _prefetch.Clear();
        DisposeWarm();
        DiscardOutside(wanted);
    }

    public void Dispose()
    {
        foreach (TAsset asset in _loaded.Values)
        {
            asset.Dispose();
        }

        _loaded.Clear();
        _prefetch.Clear();
        DisposeWarm();
        DiscardOutside(_prefetch);
    }

    private void Start(THandle handle)
    {
        if (_loaded.ContainsKey(handle) || _warm.ContainsKey(handle) || _decoding.ContainsKey(handle)
            || (_uploading is { } upload && EqualityComparer<THandle>.Default.Equals(upload.Handle, handle)))
        {
            return;
        }

        CancellationTokenSource cancel = new();
        _decoding.Add(handle, new Decode(AssetDecodes.Run(() => _decode(handle), cancel.Token), cancel));
    }

    // Waits for the decode, which rethrows its failure as the decode raised it.
    private static IPendingAsset<TAsset> Result(Decode decode)
    {
        try
        {
            return decode.Task.GetAwaiter().GetResult();
        }
        finally
        {
            decode.Release();
        }
    }

    private bool TakeFinishedPrefetch()
    {
        if (_decoding.Count == 0)
        {
            return false;
        }

        foreach (THandle handle in _prefetch)
        {
            if (!_decoding.TryGetValue(handle, out Decode? decode) || !decode.Task.IsCompleted)
            {
                continue;
            }

            _decoding.Remove(handle);

            try
            {
                _uploading = (handle, Result(decode));
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private IPendingAsset<TAsset>? TakeUpload(in THandle handle)
    {
        if (_uploading is not { } upload || !EqualityComparer<THandle>.Default.Equals(upload.Handle, handle))
        {
            return null;
        }

        _uploading = null;
        return upload.Pending;
    }

    private void DiscardOutside(HashSet<THandle> kept)
    {
        if (_uploading is { } upload && !kept.Contains(upload.Handle))
        {
            _uploading = null;
            upload.Pending.Discard();
        }

        foreach ((THandle handle, Decode decode) in _decoding.ToArray())
        {
            if (!kept.Contains(handle))
            {
                _decoding.Remove(handle);
                decode.Release();
                decode.Task.ContinueWith(
                    static finished =>
                    {
                        if (finished.IsCompletedSuccessfully)
                        {
                            finished.Result.Discard();
                        }
                        else
                        {
                            _ = finished.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private void DisposeWarm()
    {
        foreach (TAsset asset in _warm.Values)
        {
            asset.Dispose();
        }

        _warm.Clear();
    }

    private sealed class Decode(Task<IPendingAsset<TAsset>> task, CancellationTokenSource cancel)
    {
        internal Task<IPendingAsset<TAsset>> Task { get; } = task;

        // Cancels the decode if it is still waiting for its turn. One already running finishes.
        internal void Release()
        {
            cancel.Cancel();
            cancel.Dispose();
        }
    }
}

// Runs scene asset decodes on the thread pool, a few at once, leaving one core to the game thread.
internal static class AssetDecodes
{
    internal static readonly int Concurrency = Math.Clamp(Environment.ProcessorCount - 1, 1, 3);

    private static readonly SemaphoreSlim Turns = new(Concurrency);

    internal static Task<T> Run<T>(Func<T> decode, CancellationToken cancel) =>
        Task.Run(
            async () =>
            {
                await Turns.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    return decode();
                }
                finally
                {
                    Turns.Release();
                }
            },
            cancel);
}
