using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Capsule.Audio;

namespace Capsule.Build.Audio;

/// <summary>
/// Measures a shipped audio source by reading its container: how long it runs, and the loop region
/// it authors. The build knows both so that the pure mixer derives playback state from the duration
/// rather than reading anything back from a device, and the host loops a region without opening the
/// file to learn where it is.
/// </summary>
internal static class AudioProbe
{
    /// <summary>The extensions the audio domain admits, lower-case and dotted.</summary>
    internal const string WavExtension = ".wav";

    /// <inheritdoc cref="WavExtension"/>
    internal const string OggExtension = ".ogg";

    /// <summary>What the build reads out of one source's container.</summary>
    /// <param name="DurationSeconds">Seconds the source runs at unit pitch.</param>
    /// <param name="Loop">The region the source authors, or <see cref="AudioLoopRegion.None"/>.</param>
    internal readonly record struct Measurement(double DurationSeconds, AudioLoopRegion Loop);

    /// <summary>Measures <paramref name="path"/>.</summary>
    /// <exception cref="AudioFormatException">
    /// The file is malformed, truncated, of an unsupported shape, or names a loop region its own
    /// length cannot hold.
    /// </exception>
    internal static Measurement Measure(string path)
    {
        string extension = Path.GetExtension(path);

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (string.Equals(extension, WavExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Wav(stream);
        }

        if (string.Equals(extension, OggExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Ogg(stream);
        }

        throw new AudioFormatException(
            $"carries extension \"{extension}\"; audio ships as {WavExtension} or {OggExtension}.");
    }

    // RIFF: a 12-byte header, then 'id' + little-endian size + payload padded to an even length.
    // Only 'fmt ', 'data' and 'smpl' are read; every other chunk is skipped whatever it holds.
    private static Measurement Wav(FileStream stream)
    {
        Span<byte> riff = stackalloc byte[12];
        Read(stream, riff);

        if (!Is(riff[..4], "RIFF") || !Is(riff[8..12], "WAVE"))
        {
            throw new AudioFormatException("is no RIFF/WAVE file.");
        }

        Span<byte> chunk = stackalloc byte[8];
        Span<byte> format = stackalloc byte[16];

        int channels = 0;
        int bits = 0;
        uint rate = 0;
        long dataBytes = -1;
        long? loopStart = null;
        long? loopEnd = null;

        while (TryRead(stream, chunk))
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long next = stream.Position + size + (size & 1);

            if (Is(chunk[..4], "fmt "))
            {
                if (size < format.Length)
                {
                    throw new AudioFormatException(
                        $"carries a {Number(size)}-byte 'fmt ' chunk; a WAVE format chunk is at least {Number(format.Length)} bytes.");
                }

                Read(stream, format);

                int tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                if (tag is not 1 and not 3)
                {
                    throw new AudioFormatException(
                        $"is WAVE format {Number(tag)}; Capsule reads PCM (1) and IEEE float (3). Re-encode it as 16-bit PCM.");
                }

                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                rate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
            }
            else if (Is(chunk[..4], "data"))
            {
                dataBytes = size;
            }
            else if (Is(chunk[..4], "smpl"))
            {
                SampleLoop(stream, size, ref loopStart, ref loopEnd);
            }

            if (next > stream.Length)
            {
                throw new AudioFormatException("ends inside a RIFF chunk; the file is truncated.");
            }

            stream.Seek(next, SeekOrigin.Begin);
        }

        if (rate == 0 || channels == 0 || bits == 0 || bits % 8 != 0)
        {
            throw new AudioFormatException(
                "carries no usable 'fmt ' chunk; its rate, channel count or sample width is zero or not a whole number of bytes.");
        }

        if (dataBytes < 0)
        {
            throw new AudioFormatException("carries no 'data' chunk, so its length cannot be measured.");
        }

        long frames = dataBytes / (channels * (bits / 8));

        return new Measurement(frames / (double)rate, Region(loopStart, loopEnd, frames, rate));
    }

    // The 'smpl' chunk: 36 fixed bytes, the last of which count the sample loops, then 24 bytes per
    // loop. Only the first loop is read — one region is what a clip plays — and its end is the last
    // sample sounded, so the region ends one past it.
    private static void SampleLoop(FileStream stream, uint size, ref long? start, ref long? end)
    {
        const int Header = 36;
        const int Loop = 24;

        Span<byte> header = stackalloc byte[Header];
        if (size < Header)
        {
            throw new AudioFormatException(
                $"carries a {Number(size)}-byte 'smpl' chunk; a sampler chunk is at least {Number(Header)} bytes.");
        }

        Read(stream, header);

        uint loops = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        if (loops == 0)
        {
            return;
        }

        if (size < Header + Loop)
        {
            throw new AudioFormatException(
                $"declares {Number(loops)} sample loop(s) in a {Number(size)}-byte 'smpl' chunk, which holds none.");
        }

        Span<byte> loop = stackalloc byte[Loop];
        Read(stream, loop);

        start = BinaryPrimitives.ReadUInt32LittleEndian(loop[8..]);
        end = BinaryPrimitives.ReadUInt32LittleEndian(loop[12..]) + 1L;
    }

    // Ogg: a 27-byte page header, a segment table, then the segments' payload. The rate comes from
    // the Vorbis identification header the first page opens with, the frame count from the last
    // page's granule position, and the loop tags from the comment header behind it. Page CRCs are
    // not verified — the container is the build's own output of an encoder, not untrusted input.
    private static Measurement Ogg(FileStream stream)
    {
        Span<byte> page = stackalloc byte[27];
        Span<byte> segments = stackalloc byte[255];

        uint serial = 0;
        uint rate = 0;
        long frames = -1;

        // Where the comment header begins. The identification packet is alone on the first page by
        // the spec, so the next page of the same stream opens the comment one; -1 until it is seen.
        long comments = -1;
        bool opened = false;
        long header = stream.Position;

        while (TryRead(stream, page))
        {
            if (!Is(page[..4], "OggS"))
            {
                throw new AudioFormatException("is no Ogg stream, or carries a page Capsule could not find the start of.");
            }

            if (page[4] != 0)
            {
                throw new AudioFormatException($"carries an Ogg page of version {Number(page[4])}; only version 0 is defined.");
            }

            long granule = BinaryPrimitives.ReadInt64LittleEndian(page[6..14]);
            uint pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page[14..18]);
            int count = page[26];

            Read(stream, segments[..count]);

            int payload = 0;
            for (int i = 0; i < count; i++)
            {
                payload += segments[i];
            }

            long body = stream.Position;

            if (body + payload > stream.Length)
            {
                throw new AudioFormatException("ends inside an Ogg page; the file is truncated.");
            }

            if (!opened)
            {
                serial = pageSerial;
                rate = IdentificationRate(stream, payload);
                opened = true;
            }
            else if (pageSerial == serial && comments < 0)
            {
                comments = header;
            }

            // A granule of -1 says no packet finished on this page; a foreign serial is another
            // logical stream multiplexed into the same file.
            if (pageSerial == serial && granule >= 0)
            {
                frames = granule;
            }

            stream.Seek(body + payload, SeekOrigin.Begin);
            header = stream.Position;
        }

        if (!opened)
        {
            throw new AudioFormatException("holds no Ogg page at all.");
        }

        if (frames < 0)
        {
            throw new AudioFormatException("carries no Ogg granule position, so its length cannot be measured.");
        }

        long? start = null;
        long? length = null;
        long? end = null;

        if (comments >= 0)
        {
            LoopTags(stream, serial, comments, out start, out length, out end);
        }

        // LOOPSTART with LOOPLENGTH is the RPG Maker pair; LOOPSTART with LOOPEND is the same
        // authoring spelt the other way, and LOOPSTART alone loops the rest of the file.
        long? region = start is null ? null
            : length is { } run ? start + run
            : end ?? frames;

        return new Measurement(frames / (double)rate, Region(start, region, frames, rate));
    }

    // Reads the loop tags out of the Vorbis comment header, walking the whole packet across however
    // many pages carry it: each comment's length is read, and a value no loop tag can be is stepped
    // over in the file rather than gathered, so a large METADATA_BLOCK_PICTURE neither hides a tag
    // behind it nor is held in memory. A packet the file does not finish is a malformed container
    // rather than an absent region, and is refused.
    private static void LoopTags(FileStream stream, uint serial, long page, out long? start, out long? length, out long? end)
    {
        start = null;
        length = null;
        end = null;

        OggPacket packet = new(stream, serial, page);

        Span<byte> opening = stackalloc byte[7];
        packet.Read(opening);

        if (opening[0] != 3 || !Is(opening[1..], "vorbis"))
        {
            throw new AudioFormatException(
                "follows its Vorbis identification header with no comment header; Capsule reads Ogg Vorbis.");
        }

        packet.Skip(packet.ReadLength());

        uint count = packet.ReadLength();

        // A loop tag is a name, '=' and a decimal sample count, which this holds with room to spare.
        // A longer comment is stepped over from its prefix alone: no loop tag's name reaches here
        // without its whole value, and a value that would not is no whole number of samples anyway.
        Span<byte> held = stackalloc byte[96];

        for (uint i = 0; i < count; i++)
        {
            uint size = packet.ReadLength();
            int read = (int)Math.Min(size, (uint)held.Length);

            Span<byte> comment = held[..read];
            packet.Read(comment);
            packet.Skip(size - (uint)read);

            int separator = comment.IndexOf((byte)'=');
            if (separator < 0)
            {
                continue;
            }

            ReadOnlySpan<byte> name = comment[..separator];
            ReadOnlySpan<byte> value = comment[(separator + 1)..];

            if (Named(name, "LOOPSTART"))
            {
                start = Samples(name, value);
            }
            else if (Named(name, "LOOPLENGTH"))
            {
                length = Samples(name, value);
            }
            else if (Named(name, "LOOPEND"))
            {
                end = Samples(name, value);
            }
        }
    }

    // Vorbis comment names are case-insensitive ASCII.
    private static bool Named(ReadOnlySpan<byte> name, string tag)
    {
        if (name.Length != tag.Length)
        {
            return false;
        }

        for (int i = 0; i < tag.Length; i++)
        {
            if (char.ToUpperInvariant((char)name[i]) != tag[i])
            {
                return false;
            }
        }

        return true;
    }

    private static long Samples(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        string text = Encoding.UTF8.GetString(value);

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long samples))
        {
            throw new AudioFormatException(
                $"tags {Encoding.UTF8.GetString(name)}=\"{text}\"; a loop tag is a whole number of samples.");
        }

        return samples;
    }

    // The one rule a region obeys, whichever convention named it: 0 <= start < end <= the clip.
    private static AudioLoopRegion Region(long? start, long? end, long frames, uint rate)
    {
        if (start is not { } first || end is not { } last)
        {
            return AudioLoopRegion.None;
        }

        if (first < 0 || first >= last || last > frames)
        {
            throw new AudioFormatException(
                $"names a loop region of samples [{Number(first)}, {Number(last)}) in {Number(frames)} sample(s); a region starts at or after zero, ends after it starts, and ends no later than the clip does.");
        }

        return new AudioLoopRegion(first / (double)rate, last / (double)rate);
    }

    private static uint IdentificationRate(FileStream stream, int payload)
    {
        Span<byte> identification = stackalloc byte[16];

        if (payload < identification.Length)
        {
            throw new AudioFormatException("opens with a page too short to hold a Vorbis identification header.");
        }

        Read(stream, identification);

        if (identification[0] != 1 || !Is(identification[1..7], "vorbis"))
        {
            throw new AudioFormatException("opens with no Vorbis identification header; Capsule reads Ogg Vorbis.");
        }

        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(identification[12..]);
        if (rate == 0)
        {
            throw new AudioFormatException("declares a Vorbis sample rate of zero.");
        }

        return rate;
    }

    private static bool Is(ReadOnlySpan<byte> bytes, string ascii)
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

    // False only at a clean end of file; a partial read is a truncation either way.
    private static bool TryRead(Stream stream, Span<byte> buffer)
    {
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read == 0)
        {
            return false;
        }

        if (read < buffer.Length)
        {
            throw new AudioFormatException("ends inside a header; the file is truncated.");
        }

        return true;
    }

    private static void Read(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new AudioFormatException("ends where more of it was expected; the file is truncated.", ex);
        }
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    // One Ogg packet read in order across however many pages carry it, so a value inside it can be
    // stepped over in the file rather than buffered. Holds one page's segment table and nothing
    // else, whatever the packet's length. A page of a foreign serial is another logical stream
    // multiplexed into the file and is skipped; the packet continues past it.
    private sealed class OggPacket(FileStream stream, uint serial, long page)
    {
        // A segment of the maximum length says the packet continues into the next one; anything
        // shorter closes it.
        private const int Continued = 255;

        private readonly byte[] _segments = new byte[Continued];

        // Where the packet's next page begins.
        private long _page = page;

        private int _count;
        private int _index;
        private int _remaining;
        private bool _closing;

        internal void Read(Span<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                int run = Math.Min(buffer.Length, Enter());
                AudioProbe.Read(stream, buffer[..run]);
                buffer = buffer[run..];
                _remaining -= run;
            }
        }

        internal void Skip(uint bytes)
        {
            while (bytes > 0)
            {
                int run = (int)Math.Min(bytes, (uint)Enter());
                stream.Seek(run, SeekOrigin.Current);
                bytes -= (uint)run;
                _remaining -= run;
            }
        }

        internal uint ReadLength()
        {
            Span<byte> length = stackalloc byte[sizeof(uint)];
            Read(length);

            return BinaryPrimitives.ReadUInt32LittleEndian(length);
        }

        // Bytes of the packet readable at the stream's position, entering the next segment and the
        // next page as the one being read runs out. A closed packet is the end of what may be read
        // whether or not the page holds further segments: those belong to the packet behind this one.
        private int Enter()
        {
            while (_remaining == 0)
            {
                if (_closing)
                {
                    throw new AudioFormatException(
                        "ends its Vorbis comment header before the comments the header declares; the packet is truncated.");
                }

                if (_index == _count)
                {
                    OpenPage();

                    continue;
                }

                _remaining = _segments[_index++];
                _closing = _remaining < Continued;
            }

            return _remaining;
        }

        private void OpenPage()
        {
            Span<byte> header = stackalloc byte[27];

            while (true)
            {
                stream.Seek(_page, SeekOrigin.Begin);

                if (!TryRead(stream, header) || !Is(header[..4], "OggS"))
                {
                    throw new AudioFormatException(
                        "ends before the page continuing its Vorbis comment header; the file is truncated.");
                }

                int count = header[26];
                AudioProbe.Read(stream, _segments.AsSpan(0, count));

                int payload = 0;
                for (int i = 0; i < count; i++)
                {
                    payload += _segments[i];
                }

                _page = stream.Position + payload;

                if (BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]) == serial)
                {
                    _count = count;
                    _index = 0;

                    return;
                }
            }
        }
    }
}
