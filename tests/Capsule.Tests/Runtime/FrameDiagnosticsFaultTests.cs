using System.Diagnostics;
using Capsule.Diagnostics;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

// A disk that refuses a capture costs the run the capture and nothing else.
[Collection(LogSinkCollection.Name)]
public sealed class FrameDiagnosticsFaultTests : IDisposable
{
    private readonly CollectingLogSink _log = new();

    public FrameDiagnosticsFaultTests() => Log.UseSink(_log);

    public void Dispose() => Log.UseSink(null);

    // The failure is logged once, as a warning, whether the first frame's boot trace, the writer thread's full
    // buffer or disposal's partial one meets it. Every later write is dropped without a frame waiting on it.
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 10)]
    [InlineData(2, FrameDiagnostics.FlushEvery * 4)]
    public void ACaptureTheDiskRefuses_IsLoggedOnceAndTheRunGoesOn(int refusedFrom, int frames)
    {
        RefusingStream file = new();
        FrameDiagnostics diagnostics = new(new StreamWriter(file), Stopwatch.GetTimestamp(), exitAfterSeconds: null, Stopwatch.GetTimestamp);

        for (int tick = 1; tick <= frames; tick++)
        {
            file.Refusing = tick >= refusedFrom;
            Frame(diagnostics, tick);
        }

        diagnostics.Dispose();

        LogEntry fault = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, fault.Level);
        Assert.StartsWith("frame diagnostics: ", fault.Message, StringComparison.Ordinal);
        Assert.Contains(RefusingStream.Refusal, fault.Message, StringComparison.Ordinal);
    }

    private static void Frame(FrameDiagnostics diagnostics, long tick)
    {
        diagnostics.Clock.BeginUpdate(lastDrawMs: 0);
        diagnostics.BeginUpdate();
        diagnostics.Clock.EndUpdate(steps: 1);
        diagnostics.EndUpdate(tick);
        diagnostics.BeginView();
        diagnostics.BeginDraw();
        diagnostics.EndDraw();
    }

    // A file on a disk that fills once Refusing is set.
    private sealed class RefusingStream : MemoryStream
    {
        internal const string Refusal = "There is not enough space on the disk.";

        internal bool Refusing { get; set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Refuse();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Refuse();
            base.Write(buffer);
        }

        public override void Flush() => Refuse();

        private void Refuse()
        {
            if (Refusing)
            {
                throw new IOException(Refusal);
            }
        }
    }
}
