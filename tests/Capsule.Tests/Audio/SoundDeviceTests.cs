using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.Audio;
using static Capsule.Tests.Audio.AudioPlaybackFixtures;

namespace Capsule.Tests.Audio;

// A run's sound against outputs that fail or run dry, driven frame by frame as the host drives it.
[Collection(LogSinkCollection.Name)]
public sealed class SoundDeviceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // A second of mono float silence, resident.
    private static readonly AudioClip Effect = new("effect", ".wav", 1.0, default, floatSamples: true);

    private static readonly AudioClip Hum = new("loop", ".ogg", 4.0);

    private readonly CollectingLogSink _log = new();

    public SoundDeviceTests() => Log.UseSink(_log);

    public void Dispose() => Log.UseSink(null);

    // Many times the commands the ring holds are played after the fault, which a stopped mix never
    // drains. The stream it was handed goes back to the worker, and its file closes.
    [Fact]
    public void AFaultedMix_LeavesTheRunPlayingSilentlyAndRetiresItsStreams()
    {
        DrainedOutput faulty = new(failing: true);
        OutputPlatform platform = new(faulty);
        using SoundDevice device = SoundDevice.TryOpen(platform, MixThread.DefaultLeadFrames)!;
        Assert.True(faulty.Failed.Wait(Patience), "the mix did not fault in time");

        device.Player.Apply([Command(AudioCommandKind.Play, Slot(0, 1), Hum, loop: true)]);
        AudioCommand[] plays = new AudioCommand[AudioMixer.MaxVoices];
        for (int frame = 0; frame < 4 * AudioPlayer.CommandCapacity / plays.Length; frame++)
        {
            for (int slot = 0; slot < plays.Length; slot++)
            {
                plays[slot] = Command(AudioCommandKind.Play, Slot(slot, frame + 2), Effect);
            }

            device.Player.Apply(plays);
            device.Update();
        }

        Assert.Single(_log.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("mix stopped", StringComparison.Ordinal));
        Assert.True(SpinWait.SpinUntil(() => platform.HumClosed, Patience), "the stream was not retired");
    }

    // An output that played everything queued means the mix fell behind it and a gap was heard. The
    // queue filling at the start is no gap, and each gap counts once. Only the run's first is logged.
    [Fact]
    public void AnOutputTheMixLetRunDry_CountsEachGapOnceAndWarnsOfTheFirst()
    {
        DrainedOutput output = new();
        using SoundDevice device = SoundDevice.TryOpen(new OutputPlatform(output), MixThread.DefaultLeadFrames)!;
        Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not fill its lead in time");

        Assert.Equal(0, device.Underruns);

        for (int gap = 0; gap < 2; gap++)
        {
            output.Drain();
            Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not refill its lead in time");
            device.Update();
        }

        Assert.Equal(2, device.Underruns);
        Assert.Single(_log.Entries, static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("fell behind", StringComparison.Ordinal));
    }

    // A lead the game shortened is the queue the mix keeps, topped up a chunk within half the lead at a
    // time, and the platform opens its output to fit it.
    [Fact]
    public void AShortLead_IsKeptQueuedWithinHalfALeadPastIt()
    {
        const int Lead = VoiceMixer.SampleRate / 200;
        DrainedOutput output = new(Lead);
        OutputPlatform platform = new(output);
        using SoundDevice device = SoundDevice.TryOpen(platform, Lead)!;
        Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not fill its lead in time");
        Thread.Sleep(20);

        Assert.Equal(Lead, platform.LeadFrames);
        Assert.InRange(output.Queued, Lead, Lead + (Lead / 2) - 1);
    }

    // A device that takes more at a time than the lead would pull the queue dry at every pull. The mix
    // keeps one and a half of its buffer queued instead, and the log says so.
    [Fact]
    public void ALeadShorterThanTheDevicesBuffer_IsRaisedToOneAndAHalfBuffers()
    {
        const int Lead = VoiceMixer.SampleRate / 200;
        const int Device = VoiceMixer.SampleRate / 100;
        DrainedOutput output = new(Device * 3 / 2, Device);
        using SoundDevice device = SoundDevice.TryOpen(new OutputPlatform(output), Lead)!;
        Assert.True(SpinWait.SpinUntil(() => output.Full, Patience), "the mix did not fill its lead in time");
        Thread.Sleep(20);

        Assert.InRange(output.Queued, Device * 3 / 2, (Device * 9 / 4) - 1);
        Assert.Contains(_log.Entries, static entry => entry.Message.Contains("in place of the 5 ms asked for", StringComparison.Ordinal));
    }

    // The buffer the default platform asks SDL for is the largest power of two within half the lead,
    // and the default lead asks for 512 frames.
    [Theory]
    [InlineData(MixThread.DefaultLeadFrames, 512)]
    [InlineData(48_000 / 1000 * 250, 512)]
    [InlineData(48_000 / 1000 * 10, 128)]
    [InlineData(48_000 / 1000 * 5, 64)]
    public void TheBufferAskedFor_FitsWithinHalfTheLead(int leadFrames, int bufferFrames)
    {
        Assert.Equal(bufferFrames, SdlAudioOutput.BufferFramesFor(leadFrames));
    }

    // A platform that opens the one output a test hands it, and serves the effect for a .wav and the
    // shipped Ogg fixture for an .ogg.
    private sealed class OutputPlatform(AudioOutput output) : HostPlatform
    {
        private static readonly byte[] EffectFile = WavFixtures.Wav(VoiceMixer.SampleRate, 32, 1, new byte[VoiceMixer.SampleRate * sizeof(float)], formatTag: 3);

        private static readonly byte[] HumFile = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures/loop.ogg"));

        private volatile bool _humClosed;

        // The lead the open was asked to fit.
        internal int LeadFrames { get; private set; }

        // Whether a streamed play's file has closed, which happens on the decode worker.
        internal bool HumClosed => _humClosed;

        protected internal override AudioOutput? OpenAudioOutput(int leadFrames)
        {
            LeadFrames = leadFrames;

            return output;
        }

        protected internal override Stream OpenContent(string relativePath) =>
            relativePath.EndsWith(".ogg", StringComparison.Ordinal) ? new ClosingStream(HumFile, () => _humClosed = true) : new MemoryStream(EffectFile, writable: false);

        protected internal override ISaveStorage OpenSaveStorage(string localFolderName) => throw new NotSupportedException();
    }

    private sealed class ClosingStream(byte[] bytes, Action closed) : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            closed();
            base.Dispose(disposing);
        }
    }

    // An output that plays nothing until a test empties it at once, as a device does through a stall
    // longer than the mix's lead. A failing one throws from the first frames it is handed.
    private sealed class DrainedOutput(int leadFrames = MixThread.DefaultLeadFrames, int deviceFrames = 0, bool failing = false) : AudioOutput
    {
        private int _queued;

        internal ManualResetEventSlim Failed { get; } = new();

        internal bool Full => QueuedFrames >= leadFrames;

        internal int Queued => QueuedFrames;

        protected internal override int DeviceFrames => deviceFrames;

        protected internal override int QueuedFrames => Volatile.Read(ref _queued);

        internal void Drain() => Volatile.Write(ref _queued, 0);

        protected internal override void Submit(ReadOnlySpan<float> frames)
        {
            if (failing)
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
