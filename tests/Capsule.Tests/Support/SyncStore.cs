using Capsule.Runtime.Assets;

namespace Capsule.Tests;

// A scene asset store whose loader runs on the game thread, for specs about ownership.
internal static class SyncStore
{
    internal static SceneAssetStore<THandle, TAsset> Over<THandle, TAsset>(Func<THandle, TAsset> loader)
        where THandle : notnull
        where TAsset : class, IDisposable =>
        new(handle => new Loading<TAsset>(() => loader(handle)), long.MaxValue);

    // What a renderer or player does with a handle: a hit, or a first-use load.
    internal static TAsset Get<THandle, TAsset>(this SceneAssetStore<THandle, TAsset> store, in THandle handle)
        where THandle : notnull
        where TAsset : class, IDisposable =>
        store.TryGet(handle, out TAsset asset) ? asset : store.Load(handle);

    private sealed class Loading<TAsset>(Func<TAsset> load) : IPendingAsset<TAsset>
    {
        public bool Advance(ref long budget) => true;

        public TAsset Finish() => load();

        public void Discard()
        {
        }
    }
}

// An asset that records its disposal, for specs about who releases what.
internal sealed class DisposalProbe(string name = "") : IDisposable
{
    internal string Name => name;

    internal bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
