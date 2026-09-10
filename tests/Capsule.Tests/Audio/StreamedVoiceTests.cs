using System.Runtime.CompilerServices;
using Capsule.Audio;
using Capsule.Runtime.Audio;

namespace Capsule.Tests.Audio;

public sealed class StreamedVoiceTests
{
    private const int Rate = 8000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // The allocation constraint a region loop is played under: after the first play, stopping and
    // playing again reuses the retired voice whole — the same object, the same device queue and the
    // same buffers handed to it — rather than building a queue, a scratch buffer and three device
    // buffers per play.
    [Fact]
    public void AReplayedRegionLoop_SoundsOnTheRetiredVoicesOwnQueueAndBuffers()
    {
        List<FakeQueue> queues = [];
        using AudioStreamer streamer = new((rate, channels) => Opened(queues));

        PcmAudio samples = new(new float[Rate], 1, Rate);
        AudioClip clip = new("hum", ".wav", 1.0, new AudioLoopRegion(0.25, 0.75));

        StreamedVoice first = streamer.Play(samples, clip, 1f, 0f, 0f, loop: true);
        FakeQueue queue = Assert.Single(queues);
        Pump(first, queue);

        byte[][] buffers = [.. queue.Buffers];
        Assert.NotEmpty(buffers);

        first.Dispose();

        // The worker releases the source and only then pools the voice, so this is also the assertion
        // that a reused voice is one no decode still holds.
        Wait(() => streamer.Idle == 1);

        StreamedVoice second = streamer.Play(samples, clip, 1f, 0f, 0f, loop: true);
        Pump(second, queue);

        Assert.Same(first, second);
        Assert.Same(queue, Assert.Single(queues));
        Assert.Equal(0, streamer.Idle);

        Assert.Equal(buffers.Length, queue.Buffers.Count);
        for (int i = 0; i < buffers.Length; i++)
        {
            Assert.Same(buffers[i], queue.Buffers[i]);
        }
    }

    // A voice's queue and buffers are sized for one rate and channel count, so the pool is keyed by
    // format: a clip of another shape is never sounded on them.
    [Fact]
    public void AVoiceOfAnotherFormat_TakesAQueueOfItsOwn()
    {
        List<FakeQueue> queues = [];
        using AudioStreamer streamer = new((rate, channels) => Opened(queues));

        AudioClip clip = new("hum", ".wav", 1.0, new AudioLoopRegion(0.25, 0.75));

        StreamedVoice mono = streamer.Play(new PcmAudio(new float[Rate], 1, Rate), clip, 1f, 0f, 0f, loop: true);
        mono.Dispose();
        Wait(() => streamer.Idle == 1);

        StreamedVoice stereo = streamer.Play(
            new PcmAudio(new float[Rate * 2], 2, Rate),
            clip,
            1f,
            0f,
            0f,
            loop: true);

        Assert.NotSame(mono, stereo);
        Assert.Equal(2, queues.Count);
        Assert.Equal((Rate, 2), stereo.Format);
    }

    // The retention constraint the pool is under: a voice reports its retirement through a callback
    // holding the resident sound whose samples it streamed, and reads those samples through a cursor
    // of its own, so an idle voice keeping either would keep the sound decoded for the rest of the
    // run. What reaches the pool holds nothing of the play it retired from, and is still a voice.
    [Fact]
    public void ARetiredVoice_IsPooledHoldingNothingOfThePlayItRetiredFrom()
    {
        List<FakeQueue> queues = [];
        using AudioStreamer streamer = new((rate, channels) => Opened(queues));

        AudioClip clip = new("hum", ".wav", 1.0, new AudioLoopRegion(0.25, 0.75));
        StrongBox<int> retirements = new(0);

        (WeakReference Owner, WeakReference Samples) held = Retired(streamer, clip, retirements);

        // The worker releases the source and only then pools the voice, so what is idle here is what
        // the next play is handed.
        Wait(() => streamer.Idle == 1);
        Assert.Equal(1, retirements.Value);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(held.Owner.IsAlive);
        Assert.False(held.Samples.IsAlive);

        StreamedVoice replayed = streamer.Play(new PcmAudio(new float[Rate], 1, Rate), clip, 1f, 0f, 0f, loop: true);
        Pump(replayed, Assert.Single(queues));

        Assert.Equal(0, streamer.Idle);
        Assert.Equal(1, retirements.Value);
    }

    // The allocation constraint a warm replay is under: with the voice, its queue, its buffers, its
    // loop reader and its cursor over the clip's samples all pooled, playing the clip again allocates
    // nothing on the thread that plays it — the thread a game plays from is its frame's. The
    // streaming worker decodes on its own thread, whose allocations this counter does not see, which
    // is the split the design intends.
    [Fact]
    public void AWarmReplay_AllocatesNothingOnTheThreadThatPlaysIt()
    {
        List<FakeQueue> queues = [];
        using AudioStreamer streamer = new((rate, channels) => Opened(queues));

        PcmAudio samples = new(new float[Rate], 1, Rate);
        AudioClip clip = new("hum", ".wav", 1.0, new AudioLoopRegion(0.25, 0.75));

        StreamedVoice first = streamer.Play(samples, clip, 1f, 1f, 0f, loop: true);
        Pump(first, Assert.Single(queues));
        first.Dispose();
        Wait(() => streamer.Idle == 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        StreamedVoice second = streamer.Play(samples, clip, 1f, 1f, 0f, loop: true, startSeconds: 0.5);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(first, second);
        Assert.Equal(0, allocated);
    }

    // Sounds one voice over a stand-in resident sound and retires it, leaving nothing on this stack
    // that could keep that sound or its samples alive. The count it reported into outlives it, so the
    // retirement is still readable once the sound itself is gone.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Owner, WeakReference Samples) Retired(
        AudioStreamer streamer,
        in AudioClip clip,
        StrongBox<int> retirements)
    {
        PcmAudio samples = new(new float[Rate], 1, Rate);
        Resident resident = new(retirements);

        streamer.Play(samples, clip, 1f, 0f, 0f, loop: true, retired: resident.Retire).Dispose();

        return (new WeakReference(resident), new WeakReference(samples));
    }

    private static FakeQueue Opened(List<FakeQueue> queues)
    {
        FakeQueue queue = new();
        queues.Add(queue);

        return queue;
    }

    // Plays the voice out for a few frames: the worker decodes ahead, the queue empties as a device
    // would empty it, and the voice hands it what it has room for.
    private static void Pump(StreamedVoice voice, FakeQueue queue)
    {
        Wait(() =>
        {
            queue.PlayOut();
            voice.Update();

            return queue.Buffers.Count >= 3;
        });
    }

    private static void Wait(Func<bool> held)
    {
        DateTime deadline = DateTime.UtcNow + Patience;

        while (!held())
        {
            Assert.True(DateTime.UtcNow < deadline, "the streaming worker did not get there in time");
            Thread.Sleep(1);
        }
    }

    // Stands in for the resident sound a streamed region loop is counted against: the retirement
    // callback's target, and the count that outlives it.
    private sealed class Resident(StrongBox<int> retirements)
    {
        internal void Retire() => retirements.Value++;
    }

    // A device queue that counts rather than sounds, and remembers every distinct buffer it was
    // handed: what the voice reusing its own buffers is read off.
    private sealed class FakeQueue : IPcmQueue
    {
        private int _pending;

        internal List<byte[]> Buffers { get; } = [];

        public int Pending => Volatile.Read(ref _pending);

        public bool Stopped { get; private set; } = true;

        public void SetGain(float gain)
        {
        }

        public void SetPitch(float pitch)
        {
        }

        public void SetPan(float pan)
        {
        }

        public void Submit(byte[] buffer, int bytes)
        {
            // Contains rather than a predicate: the game thread submits inside the window a replay's
            // allocations are measured in, and a closure over the buffer would be one of them.
            if (!Buffers.Contains(buffer))
            {
                Buffers.Add(buffer);
            }

            Interlocked.Increment(ref _pending);
        }

        public void Play() => Stopped = false;

        public void Pause() => Stopped = true;

        public void Resume() => Stopped = false;

        public void Stop()
        {
            Stopped = true;
            Volatile.Write(ref _pending, 0);
        }

        public void Dispose() => Stop();

        // One frame of the device consuming what it was queued.
        internal void PlayOut() => Volatile.Write(ref _pending, 0);
    }
}
