using System.Globalization;
using Capsule.Audio;

namespace Capsule.Build.Audio;

/// <summary>
/// Measures a shipped audio source by reading its container: how long it runs, and the loop region
/// it authors. The mixer derives playback state from the measured duration instead of reading a
/// device, and the host loops a region without opening the file to find it.
/// </summary>
internal static class AudioProbe
{
    /// <summary>What the build reads out of one source's container.</summary>
    /// <param name="DurationSeconds">Seconds the source runs at unit pitch.</param>
    /// <param name="Loop">The region the source authors, or <see cref="AudioLoopRegion.None"/>.</param>
    /// <param name="FloatSamples">Whether the source is a WAVE of float samples, which ships as authored instead of as FLAC.</param>
    internal readonly record struct Measurement(double DurationSeconds, AudioLoopRegion Loop, bool FloatSamples = false);

    /// <summary>Measures <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">
    /// The file is malformed, truncated, of an unsupported shape, or names a loop region its own
    /// length cannot hold.
    /// </exception>
    internal static Measurement Measure(string path)
    {
        string extension = Path.GetExtension(path);

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (string.Equals(extension, WavProbe.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return WavProbe.Measure(stream);
        }

        if (string.Equals(extension, OggProbe.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return OggProbe.Measure(stream);
        }

        throw new FormatException(
            $"carries extension \"{extension}\". Audio ships as {WavProbe.Extension} or {OggProbe.Extension}. Re-encode it as one of them.");
    }

    // The rule a region obeys under every convention: 0 <= start < end <= the clip's length.
    internal static AudioLoopRegion Region(long? start, long? end, long frames, uint rate)
    {
        if (start is not { } first || end is not { } last)
        {
            return AudioLoopRegion.None;
        }

        if (first < 0 || first >= last || last > frames)
        {
            throw new FormatException(
                $"names a loop region of samples [{Number(first)}, {Number(last)}) in {Number(frames)} sample(s). A region starts at or after zero, ends after it starts, and ends no later than the clip.");
        }

        return new AudioLoopRegion(first / (double)rate, last / (double)rate);
    }

    // The host plays mono and stereo and refuses anything wider when the clip first plays.
    internal static void RequireMonoOrStereo(int channels)
    {
        if (channels is not (1 or 2))
        {
            throw new FormatException($"carries {Number(channels)} channels. Capsule plays mono and stereo. Mix it down to stereo.");
        }
    }

    internal static bool Is(ReadOnlySpan<byte> bytes, string ascii)
    {
        for (int i = 0; i < ascii.Length; i++)
        {
            if (bytes[i] != (byte)ascii[i])
            {
                return false;
            }
        }

        return true;
    }

    // False only at a clean end of file. A partial read is a truncation.
    internal static bool TryRead(Stream stream, Span<byte> buffer)
    {
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read == 0)
        {
            return false;
        }

        if (read < buffer.Length)
        {
            throw new FormatException("ends inside a header. The file is truncated. Export it again.");
        }

        return true;
    }

    internal static void Read(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new FormatException("ends where more of it was expected. The file is truncated. Export it again.", ex);
        }
    }

    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
