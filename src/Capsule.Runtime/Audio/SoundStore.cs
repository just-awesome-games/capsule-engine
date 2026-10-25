using Capsule.Assets;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Runtime.Assets;

namespace Capsule.Runtime.Audio;

// The resident clips owned by the current scene, preloaded where declared and otherwise loaded on
// first play, and the opening of a streamed clip's file per play. Streamed clips are never resident.
//
// A voice outlives the scene that started it, because the mixer is the run's and not the scene's. It
// holds its own reference to the samples it plays. A scene releasing a clip only drops the store's.
internal sealed class SoundStore : IDisposable
{
    private readonly Func<AudioClip, IPcmSource> _open;
    private readonly SceneAssetStore<AudioClip, PcmAudio> _resident;

    // read decodes a resident clip whole on the thread pool, and open opens a streamed clip on the game
    // thread. Each throws for a missing or malformed file, which is a build fault and not a device one.
    internal SoundStore(Func<AudioClip, PcmAudio> read, Func<AudioClip, IPcmSource> open)
    {
        _open = open;
        _resident = new(clip => new PendingClip(read(clip)), long.MaxValue);
    }

    // Missing preloads are decoded before the prior scene's sounds are released. Streamed clips are
    // dropped here instead of loaded. Declaring one is not an error and reserves nothing.
    internal void ChangeScene(AssetCollection preloads) => _resident.ChangeScene(ResidentOf(preloads));

    internal void Prefetch(AssetCollection preloads) => _resident.Prefetch(ResidentOf(preloads));

    internal void Pump() => _resident.Pump();

    // A resident clip's samples, loaded on first use when the scene did not preload the clip.
    internal PcmAudio Get(in AudioClip clip)
    {
        if (_resident.TryGet(clip, out PcmAudio? samples))
        {
            return samples;
        }

        samples = _resident.Load(clip);
        Log.Info($"'{clip.Name}' loaded on first play. Declare it to preload it");

        return samples;
    }

    // A streamed clip's decoder, opened for one play.
    internal IPcmSource Open(in AudioClip clip) => _open(clip);

    public void Dispose() => _resident.Dispose();

    private static List<AudioClip> ResidentOf(AssetCollection preloads)
    {
        List<AudioClip> resident = [];
        foreach (AudioClip clip in preloads.Clips)
        {
            if (!clip.IsStreamed)
            {
                resident.Add(clip);
            }
        }

        return resident;
    }

    private sealed class PendingClip(PcmAudio samples) : IPendingAsset<PcmAudio>
    {
        public bool Advance(ref long budget) => true;

        public PcmAudio Finish() => samples;

        public void Discard()
        {
        }
    }
}
