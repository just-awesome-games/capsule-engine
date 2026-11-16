using System.Diagnostics;
using System.Globalization;
using System.Text;
using Capsule.Diagnostics;
using Capsule.Runtime.Gpu;

namespace Capsule.Runtime.DevTools;

// In a development build, unless the Capsule.FrameTrap switch is false, every windowed frame downloads the
// surface a capture reads into a ring of readbacks, with what the frame drew, and a dump writes the ring out
// as PNGs with one table of those facts.
// Recording never waits for the GPU, and a slot is downloaded into again only a whole ring later, long after
// the frame that last filled it has completed. A dump waits for the GPU to go idle, and may hitch.
internal sealed class FrameTrap : IDisposable
{
    // How much history the ring aims to hold at the display's rate, within its byte budget, and its least length.
    private const double HistorySeconds = 2;
    private const long BudgetBytes = 1536L * 1024 * 1024;
    private const int LeastFrames = 16;

    // The rate a display that reports none is taken to refresh at.
    private const double FallbackRate = 60;

    // On unless the switch is set false, which a measurement of frame cost does.
    private static readonly bool Switch = !AppContext.TryGetSwitch("Capsule.FrameTrap", out bool on) || on;

    private readonly GpuDevice _device;
    private readonly string _directory;
    private readonly Func<double> _displayRate;

    private GpuReadback[] _ring = [];
    private Slot[] _slots = [];

    // Frames downloaded into the current ring, and frames recorded since the trap started.
    private long _filled;
    private long _number;

    private bool _warnedNoSurface;

    // directory: where each dump makes its own folder. displayRate: the display's refresh rate in hertz, or 0
    // where it cannot say.
    internal FrameTrap(GpuDevice device, string directory, Func<double> displayRate)
    {
        _device = device;
        _directory = directory;
        _displayRate = displayRate;
        Log.Info($"The frame trap is on. F9 writes its recent frames into a new folder under {directory}");
    }

    internal enum Walk
    {
        Recorded,
        Reused,
        Diverged,
    }

    // Whether the trap runs: a development build whose switch is not false.
    internal static bool IsOn => Development.IsSupported && Switch;

    // How many frames a ring of width x height surfaces holds at rate hertz.
    internal static int RingLength(double rate, int width, int height)
    {
        long bytes = (long)width * height * 4;
        double wanted = Math.Ceiling(HistorySeconds * (rate > 0 ? rate : FallbackRate));
        long affordable = BudgetBytes / bytes;

        return (int)Math.Max(LeastFrames, Math.Min(wanted, affordable));
    }

    // Records the download of surface into the next slot, after the frame's render passes in commands. A null
    // surface is a frame with nothing to keep.
    internal void Record(GpuCommandBuffer commands, GpuTexture? surface, in Facts facts)
    {
        if (surface is null)
        {
            if (!_warnedNoSurface)
            {
                _warnedNoSurface = true;
                Log.Warning("The frame trap keeps nothing: it needs a render resolution, or a frame drawn offscreen.");
            }

            return;
        }

        if (_ring.Length == 0 || _ring[0].Width != surface.Width || _ring[0].Height != surface.Height)
        {
            Resize(surface);
        }

        int slot = (int)(_filled % _ring.Length);
        GpuCopyPass copy = commands.BeginCopyPass();
        copy.DownloadFromTexture(surface, _ring[slot]);
        copy.End();
        _slots[slot] = new Slot(_number++, Stopwatch.GetTimestamp(), facts);
        _filled++;
    }

    // Writes every slot oldest first as frame-<number>.png, with frames.tsv, into a new folder named for now.
    // Called between frames, with no command buffer open. A failure is logged, and the trap keeps recording.
    internal unsafe void Dump()
    {
        int held = (int)Math.Min(_filled, _ring.Length);
        if (held == 0)
        {
            Log.Info("The frame trap has no frames to write yet.");
            return;
        }

        long first = _filled - held;
        nint[] texels = new nint[held];
        int mapped = 0;
        try
        {
            _device.WaitForIdle();
            string folder = Path.Combine(_directory, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);

            for (; mapped < held; mapped++)
            {
                texels[mapped] = (nint)Slotted(first + mapped).Map();
            }

            Parallel.For(0, held, index =>
            {
                GpuReadback readback = Slotted(first + index);
                ReadOnlySpan<byte> pixels = new((byte*)texels[index], readback.Width * readback.Height * 4);
                byte[] png = GpuReadback.EncodePng(pixels, readback.Width, readback.Height, readback.Format);
                File.WriteAllBytes(Path.Combine(folder, FileName(_slots[Index(first + index)].Number)), png);
            });

            File.WriteAllText(Path.Combine(folder, "frames.tsv"), Table(first, held));
            Log.Info($"The frame trap wrote {held} frames to {folder}");
        }
        catch (Exception error)
        {
            Log.Warning($"The frame trap's dump failed: {error.Message}");
        }
        finally
        {
            for (int index = 0; index < mapped; index++)
            {
                Slotted(first + index).Unmap();
            }
        }
    }

    public void Dispose()
    {
        foreach (GpuReadback readback in _ring)
        {
            readback.Dispose();
        }

        _ring = [];
    }

    private static string FileName(long number) => string.Create(CultureInfo.InvariantCulture, $"frame-{number:D7}.png");

    // A ring of another size starts empty. SDL releases a readback once the frames downloading into it complete.
    private void Resize(GpuTexture surface)
    {
        Dispose();
        int length = RingLength(_displayRate(), surface.Width, surface.Height);
        _ring = new GpuReadback[length];
        for (int index = 0; index < length; index++)
        {
            _ring[index] = _device.CreateReadback(surface);
        }

        _slots = new Slot[length];
        _filled = 0;
        long megabytes = (long)length * surface.Width * surface.Height * 4 / (1024 * 1024);
        Log.Info($"The frame trap keeps the last {length} frames of its {surface.Width}x{surface.Height} surface, {megabytes} MB.");
    }

    private int Index(long frame) => (int)(frame % _ring.Length);

    private GpuReadback Slotted(long frame) => _ring[Index(frame)];

    private string Table(long first, int held)
    {
        StringBuilder table = new("frame\tms\tdelta_ms\talpha\twalk\tkept\tframe_instances\tdraws\tstaged_bytes\tstaging_overflowed\ttextures_uploaded\n");
        long previous = _slots[Index(first)].Timestamp;
        for (int index = 0; index < held; index++)
        {
            Slot slot = _slots[Index(first + index)];
            Facts facts = slot.Facts;
            table.Append(CultureInfo.InvariantCulture, $"{slot.Number}\t{Milliseconds(slot.Timestamp):F3}\t{Milliseconds(slot.Timestamp - previous):F3}\t{facts.Alpha:F4}\t{facts.Walk}\t");
            table.Append(CultureInfo.InvariantCulture, $"{facts.Kept}\t{facts.FrameInstances}\t{facts.Draws}\t{facts.Staged}\t{facts.Overflowed}\t{facts.TexturesUploaded}\n");
            previous = slot.Timestamp;
        }

        return table.ToString();

        static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }

    // What a frame drew: its fraction of a step, how its draw list walked, the walk's kept instances, its
    // instances for the frame alone and its draws, the bytes the frame staged and whether staging overflowed,
    // and whether the texture store uploaded anything.
    internal readonly record struct Facts(
        float Alpha,
        Walk Walk,
        int Kept,
        int FrameInstances,
        int Draws,
        int Staged,
        bool Overflowed,
        bool TexturesUploaded);

    private readonly record struct Slot(long Number, long Timestamp, Facts Facts);
}
