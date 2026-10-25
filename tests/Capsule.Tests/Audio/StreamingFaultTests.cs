using System.Collections.Concurrent;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Runtime.Audio;

namespace Capsule.Tests.Audio;

// A log sink is written on the simulation thread only. A fault on the streaming worker is
// recorded there and logged by the game thread's next call into the streamer or the stream.
[Collection(LogSinkCollection.Name)]
public sealed class StreamingFaultTests : IDisposable
{
    private const int Rate = 8000;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ThreadSink _sink = new();

    public StreamingFaultTests() => Log.UseSink(_sink);

    public void Dispose() => Log.UseSink(null);

    [Fact]
    public void AStreamThatFaultsOnTheWorker_IsLoggedOnTheGameThread()
    {
        using AudioStreamer streamer = new();
        AudioClip clip = new("hum", ".ogg", 1.0, default);

        // The first read is the game thread's, inside Start. The worker's next read throws.
        AudioStream stream = streamer.Play(new FailingSource(readsBeforeFault: 1), clip, loop: true);

        Assert.True(SpinWait.SpinUntil(() => stream.Ended, Patience), "the stream did not fault in time");
        Assert.Empty(_sink.Lines);

        streamer.Update();
        streamer.Update();

        (int thread, string message) = Assert.Single(_sink.Lines);
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
        Assert.Contains("'hum'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoppedWorker_IsLoggedOnTheGameThread()
    {
        using AudioStreamer streamer = new();
        AudioClip clip = new("hum", ".ogg", 1.0, default);

        // Closing the source happens outside Fill's containment, which stops the worker.
        streamer.Play(new FailingSource(readsBeforeFault: int.MaxValue, failOnDispose: true), clip, loop: true).Retire();

        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    streamer.Update();

                    return _sink.Lines.Count > 0;
                },
                Patience),
            "the worker fault was not logged in time");

        (int thread, string message) = Assert.Single(_sink.Lines);
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
        Assert.Contains("streaming worker stopped", message, StringComparison.Ordinal);
    }

    // The worker's decode fails after the stream was retired. The pooled stream's next play hears it.
    [Fact]
    public void AFaultWhileAStreamRetires_IsLoggedOnTheGameThreadWhenItPlaysAgain()
    {
        using AudioStreamer streamer = new();
        AudioClip clip = Retire(streamer);

        streamer.Play(new FailingSource(readsBeforeFault: int.MaxValue), clip, loop: true).Retire();

        (int thread, string message) = Assert.Single(_sink.Lines);
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
        Assert.Contains("'hum'", message, StringComparison.Ordinal);
    }

    // The same fault on a stream that never plays again is heard when the streamer is torn down.
    [Fact]
    public void AFaultWhileAStreamRetires_IsLoggedOnTheGameThreadAtTeardown()
    {
        AudioStreamer streamer = new();
        _ = Retire(streamer);

        streamer.Dispose();

        (int thread, string message) = Assert.Single(_sink.Lines);
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
        Assert.Contains("'hum'", message, StringComparison.Ordinal);
    }

    // Plays a clip whose worker read fails only once the stream is retired, and waits for it to pool.
    private AudioClip Retire(AudioStreamer streamer)
    {
        AudioClip clip = new("hum", ".ogg", 1.0, default);
        HeldFaultSource source = new();

        AudioStream stream = streamer.Play(source, clip, loop: true);
        Assert.True(source.Reading.Wait(Patience), "the worker did not read in time");
        stream.Retire();
        source.Release.Set();

        Assert.True(SpinWait.SpinUntil(() => streamer.Pooled == 1, Patience), "the stream did not retire in time");
        Assert.Empty(_sink.Lines);

        return clip;
    }

    private sealed class ThreadSink : ILogSink
    {
        internal ConcurrentQueue<(int Thread, string Message)> Lines { get; } = new();

        public void Write(LogLevel level, string message) => Lines.Enqueue((Environment.CurrentManagedThreadId, message));
    }

    private sealed class FailingSource(int readsBeforeFault, bool failOnDispose = false) : IPcmSource
    {
        private int _reads;
        private bool _failOnDispose = failOnDispose;

        public int Channels => 1;

        public int SampleRate => Rate;

        public void SeekTo(long frame)
        {
        }

        public int Read(Span<float> target)
        {
            if (_reads++ >= readsBeforeFault)
            {
                throw new InvalidDataException("corrupt packet");
            }

            target.Clear();

            return target.Length;
        }

        public void Dispose()
        {
            if (_failOnDispose)
            {
                _failOnDispose = false;
                throw new IOException("handle lost");
            }
        }
    }

    // Lets the game thread's read inside Start through. Holds the worker's next read until released, then fails it.
    private sealed class HeldFaultSource : IPcmSource
    {
        private int _reads;

        internal ManualResetEventSlim Reading { get; } = new();

        internal ManualResetEventSlim Release { get; } = new();

        public int Channels => 1;

        public int SampleRate => Rate;

        public void SeekTo(long frame)
        {
        }

        public int Read(Span<float> target)
        {
            if (_reads++ == 0)
            {
                target.Clear();

                return target.Length;
            }

            Reading.Set();
            Release.Wait(Patience);

            throw new InvalidDataException("corrupt packet");
        }

        public void Dispose()
        {
        }
    }
}
