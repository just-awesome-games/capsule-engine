using System.Runtime.InteropServices;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// A run's sound against outputs that fail or lose their device, driven frame by frame as the host
// drives it. The mix thread runs, paced by the clock as a device paces it.
[Collection(LogSinkCollection.Name)]
public sealed class SoundDeviceTests : IDisposable
{
    private const double FrameSeconds = 1.0 / 60.0;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Ten seconds of mono float samples rising by one step a frame, which makes every sample the
    // output is handed name the frame the voice had reached.
    private static readonly AudioClip Ramp = new("ramp", ".wav", 10.0, default, floatSamples: true);

    private static readonly AudioClip Hum = new("loop", ".ogg", 4.0);

    private readonly CollectingLogSink _log = new();

    public SoundDeviceTests() => Log.UseSink(_log);

    public void Dispose() => Log.UseSink(null);

    // Many times the commands the ring holds are played after the fault, which a stopped mix never
    // drains. The stream it was handed goes back to the worker, and its file closes.
    [Fact]
    public void AFaultedMix_LeavesTheRunPlayingSilentlyAndRetiresItsStreams()
    {
        PacedOutput faulty = new(fails: true);
        OutputPlatform platform = new(faulty);
        using SoundDevice device = SoundDevice.TryOpen(platform)!;
        Assert.True(faulty.Failed.Wait(Patience), "the mix did not fault in time");

        device.Player.Apply([Command(AudioCommandKind.Play, Slot(0, 1), Hum, loop: true)]);
        AudioCommand[] plays = new AudioCommand[AudioMixer.MaxVoices];
        for (int frame = 0; frame < 4 * AudioPlayer.CommandCapacity / plays.Length; frame++)
        {
            for (int slot = 0; slot < plays.Length; slot++)
            {
                plays[slot] = Command(AudioCommandKind.Play, Slot(slot, frame + 2), Ramp);
            }

            device.Player.Apply(plays);
            device.Update(FrameSeconds);
        }

        Assert.Single(_log.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("mix stopped", StringComparison.Ordinal));
        Assert.True(SpinWait.SpinUntil(() => platform.HumClosed, Patience), "the stream was not retired");
    }

    // The lost output closes and the platform's default is asked for again. A failed reopen is warned
    // of once and retried on the interval. The reopened output hears the voice from past where the
    // lost one left it, because the mixer kept time over the gap.
    [Fact]
    public void ALostOutput_IsReopenedOnTheIntervalAndEveryVoicePlaysOn()
    {
        PacedOutput lost = new();
        OutputPlatform platform = new(lost);
        using SoundDevice device = SoundDevice.TryOpen(platform)!;
        device.Player.Apply([Command(AudioCommandKind.Play, Slot(0, 1), Ramp)]);
        Assert.True(SpinWait.SpinUntil(() => lost.Last > 0f, Patience), "the voice was not heard in time");

        lost.Disconnect();
        device.Update(SoundDevice.WatchSeconds);
        device.Update(SoundDevice.WatchSeconds / 2);

        Assert.True(lost.Disposed);
        Assert.Equal(2, platform.Opens);

        device.Update(SoundDevice.WatchSeconds / 2);

        Assert.Equal(3, platform.Opens);

        PacedOutput reopened = new();
        platform.Outputs.Enqueue(reopened);
        device.Update(SoundDevice.WatchSeconds);

        Assert.True(SpinWait.SpinUntil(() => reopened.Heard, Patience), "the reopened output was not fed in time");
        Assert.True(reopened.First > lost.Last, $"the voice restarted at {reopened.First}, short of {lost.Last}");
        Assert.Single(_log.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Contains(_log.Entries, static entry => entry.Message.Contains("reopened", StringComparison.Ordinal));
    }

    // An output that played everything queued means the mix fell behind it and a gap was heard. The
    // queue filling at the start is no gap, and each gap counts once. Only the run's first is logged.
    [Fact]
    public void AnOutputTheMixLetRunDry_CountsEachGapOnceAndWarnsOfTheFirst()
    {
        DrainedOutput output = new();
        using SoundDevice device = SoundDevice.TryOpen(new OutputPlatform(output))!;
        Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not fill its lead in time");

        Assert.Equal(0, device.Underruns);

        for (int gap = 0; gap < 2; gap++)
        {
            output.Drain();
            Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not refill its lead in time");
            device.Update(FrameSeconds);
        }

        Assert.Equal(2, device.Underruns);
        Assert.Single(_log.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("fell behind", StringComparison.Ordinal));
    }

    // A mix thread that faulted is kept when its lost output is replaced, and the gaps it counted are
    // counted once.
    [Fact]
    public void AFaultedMix_CountsItsGapsOnceWhenItsOutputIsLost()
    {
        DrainedOutput output = new();
        using SoundDevice device = SoundDevice.TryOpen(new OutputPlatform(output))!;
        Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not fill its lead in time");

        output.Drain(failing: true);
        Assert.True(output.Failed.Wait(Patience), "the mix did not fault in time");
        output.Disconnect();
        device.Update(SoundDevice.WatchSeconds);

        Assert.Equal(1, device.Underruns);
    }

    // A platform that hands out the outputs a test queues and then none, and serves the ramp for a
    // .wav and the shipped Ogg fixture for an .ogg.
    private sealed class OutputPlatform : HostPlatform
    {
        private static readonly byte[] RampFile = RampWave();

        private static readonly byte[] HumFile = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures/loop.ogg"));

        private volatile bool _humClosed;

        internal Queue<AudioOutput> Outputs { get; } = new();

        internal int Opens { get; private set; }

        internal OutputPlatform(AudioOutput first) => Outputs.Enqueue(first);

        // Whether a streamed play's file has closed, which happens on the decode worker.
        internal bool HumClosed => _humClosed;

        protected internal override AudioOutput? OpenAudioOutput()
        {
            Opens++;

            return Outputs.TryDequeue(out AudioOutput? output) ? output : null;
        }

        protected internal override Stream OpenContent(string relativePath) =>
            relativePath.EndsWith(".ogg", StringComparison.Ordinal) ? new ClosingStream(HumFile, () => _humClosed = true) : new MemoryStream(RampFile, writable: false);

        protected internal override ISaveStorage OpenSaveStorage(string localFolderName) => throw new NotSupportedException();

        private static byte[] RampWave()
        {
            float[] samples = new float[(int)(Ramp.DurationSeconds * VoiceMixer.SampleRate)];
            for (int frame = 0; frame < samples.Length; frame++)
            {
                samples[frame] = frame / (float)(1 << 22);
            }

            return WavFixtures.Wav(VoiceMixer.SampleRate, 32, 1, MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), formatTag: 3);
        }
    }

    private sealed class ClosingStream(byte[] bytes, Action closed) : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            closed();
            base.Dispose(disposing);
        }
    }

    // An output the clock drains as a device would, which a test can disconnect or have fail on the
    // first frames it is handed. A disconnected output stops draining, as a dead device may. It keeps
    // the first and the latest sample it was handed.
    private sealed class PacedOutput(bool fails = false) : AudioOutput
    {
        private readonly SilentOutput _pace = new();
        private volatile bool _connected = true;
        private volatile bool _heard;

        // The frames held since the disconnect, which no clock drains.
        private int _stalled;
        private float _first;
        private float _last;

        internal ManualResetEventSlim Failed { get; } = new();

        internal bool Heard => _heard;

        // Read once Heard is true.
        internal float First => _first;

        internal float Last => Volatile.Read(ref _last);

        internal bool Disposed { get; private set; }

        protected internal override int QueuedFrames => _connected ? _pace.QueuedFrames : Volatile.Read(ref _stalled);

        protected internal override bool Connected => _connected;

        internal void Disconnect()
        {
            Volatile.Write(ref _stalled, _pace.QueuedFrames);
            _connected = false;
        }

        public override void Dispose() => Disposed = true;

        protected internal override void Submit(ReadOnlySpan<float> frames)
        {
            if (fails)
            {
                Failed.Set();
                throw new InvalidOperationException("device fault");
            }

            if (!_heard)
            {
                _first = frames[0];
                _heard = true;
            }

            Volatile.Write(ref _last, frames[^1]);
            if (!_connected)
            {
                Interlocked.Add(ref _stalled, frames.Length / Channels);

                return;
            }

            _pace.Submit(frames);
        }
    }

    // An output that plays nothing until a test empties it at once, as a device does through a stall
    // longer than the mix's lead.
    private sealed class DrainedOutput : AudioOutput
    {
        private int _queued;
        private volatile bool _failing;
        private volatile bool _connected = true;

        internal ManualResetEventSlim Failed { get; } = new();

        internal bool Full => QueuedFrames >= MixThread.LeadFrames;

        protected internal override int QueuedFrames => Volatile.Read(ref _queued);

        protected internal override bool Connected => _connected;

        // A failing drain has the next submit throw, after the mix has counted the gap.
        internal void Drain(bool failing = false)
        {
            _failing = failing;
            Volatile.Write(ref _queued, 0);
        }

        internal void Disconnect() => _connected = false;

        protected internal override void Submit(ReadOnlySpan<float> frames)
        {
            if (_failing)
            {
                Failed.Set();
                throw new InvalidOperationException("device fault");
            }

            Interlocked.Add(ref _queued, frames.Length / Channels);
        }

        public override void Dispose()
        {
        }
    }
}
