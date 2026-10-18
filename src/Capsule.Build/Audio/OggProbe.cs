using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using static Capsule.Build.Audio.AudioProbe;

namespace Capsule.Build.Audio;

/// <summary>Measures an Ogg Vorbis source: its length from the last granule position and its loop from the comment header's tags.</summary>
internal static class OggProbe
{
    /// <summary>The extension an Ogg Vorbis source is admitted by, lower-case and dotted.</summary>
    internal const string Extension = ".ogg";

    // The fixed bytes of a page header, before its segment table.
    private const int PageHeader = 27;

    // The most segments a page holds, and the length of a segment that continues its packet.
    private const int Continued = 255;

    // Ogg: a 27-byte page header, a segment table, then the segments' payload. The rate comes from
    // the Vorbis identification header the first page opens with, the frame count from the last
    // page's granule position, and the loop tags from the comment header behind it. Page CRCs are
    // not verified. The container is an encoder's output on the build host.
    internal static Measurement Measure(FileStream stream)
    {
        Span<byte> segments = stackalloc byte[Continued];

        uint serial = 0;
        uint rate = 0;
        long frames = -1;

        // Where the comment header begins. The spec puts the identification packet alone on the
        // first page. The next page of the same stream opens the comment header. -1 until seen.
        long comments = -1;
        bool opened = false;
        long header = stream.Position;

        while (TryReadPage(stream, segments, out Page page))
        {
            long body = stream.Position;

            if (body + page.Payload > stream.Length)
            {
                throw new FormatException("ends inside an Ogg page. The file is truncated. Export it again.");
            }

            if (!opened)
            {
                serial = page.Serial;
                rate = IdentificationRate(stream, page.Payload);
                opened = true;
            }
            else if (page.Serial == serial && comments < 0)
            {
                comments = header;
            }

            // A granule of -1 says no packet finished on this page. A foreign serial belongs to
            // another logical stream multiplexed into the same file.
            if (page.Serial == serial && page.Granule >= 0)
            {
                frames = page.Granule;
            }

            stream.Seek(body + page.Payload, SeekOrigin.Begin);
            header = stream.Position;
        }

        if (!opened)
        {
            throw new FormatException("holds no Ogg page. Export it as Ogg Vorbis.");
        }

        if (frames < 0)
        {
            throw new FormatException("carries no Ogg granule position, and its length cannot be measured. Export it again.");
        }

        long? start = null;
        long? length = null;
        long? end = null;

        if (comments >= 0)
        {
            LoopTags(stream, serial, comments, out start, out length, out end);
        }

        // LOOPSTART with LOOPLENGTH is the common pair. LOOPSTART with LOOPEND is the same
        // authoring spelt the other way, and LOOPSTART alone loops the rest of the file.
        long? region = start is null ? null
            : length is { } run ? start + run
            : end ?? frames;

        return new Measurement(frames / (double)rate, Region(start, region, frames, rate));
    }

    // Reads one page's header and segment table and leaves the stream at its payload. False only at
    // a clean end of file.
    private static bool TryReadPage(Stream stream, Span<byte> segments, out Page page)
    {
        Span<byte> header = stackalloc byte[PageHeader];
        if (!TryRead(stream, header))
        {
            page = default;
            return false;
        }

        if (!Is(header[..4], "OggS"))
        {
            throw new FormatException("is no Ogg stream, or carries a page Capsule could not find the start of. Export it as Ogg Vorbis.");
        }

        if (header[4] != 0)
        {
            throw new FormatException($"carries an Ogg page of version {Number(header[4])}. Only version 0 is defined. Export it as Ogg Vorbis.");
        }

        int count = header[26];
        AudioProbe.Read(stream, segments[..count]);

        int payload = 0;
        for (int i = 0; i < count; i++)
        {
            payload += segments[i];
        }

        page = new Page(
            BinaryPrimitives.ReadInt64LittleEndian(header[6..14]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]),
            count,
            payload);

        return true;
    }

    // Reads the loop tags out of the Vorbis comment header, walking the packet across however many
    // pages carry it. Each comment's length is read, and a value too long to be a loop tag is
    // stepped over in the file. A large METADATA_BLOCK_PICTURE neither hides a tag behind it nor is
    // held in memory. A packet the file does not finish is refused as a malformed container.
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
            throw new FormatException(
                "follows its Vorbis identification header with no comment header. Capsule reads Ogg Vorbis. Export it as Ogg Vorbis.");
        }

        packet.Skip(packet.ReadLength());

        uint count = packet.ReadLength();

        // A loop tag is a name, '=' and a decimal sample count, which this buffer holds with room to
        // spare. A longer comment is stepped over from its prefix alone. A value that does not fit
        // here is not a whole number of samples.
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
            throw new FormatException(
                $"tags {Encoding.UTF8.GetString(name)}=\"{text}\". A loop tag is a whole number of samples. Write the sample count in decimal.");
        }

        return samples;
    }

    private static uint IdentificationRate(FileStream stream, int payload)
    {
        Span<byte> identification = stackalloc byte[16];

        if (payload < identification.Length)
        {
            throw new FormatException("opens with a page too short to hold a Vorbis identification header. Export it as Ogg Vorbis.");
        }

        AudioProbe.Read(stream, identification);

        if (identification[0] != 1 || !Is(identification[1..7], "vorbis"))
        {
            throw new FormatException("opens with no Vorbis identification header. Capsule reads Ogg Vorbis. Export it as Ogg Vorbis.");
        }

        RequireMonoOrStereo(identification[11]);

        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(identification[12..]);
        if (rate == 0)
        {
            throw new FormatException("declares a Vorbis sample rate of zero. Export it again at the rate it plays at.");
        }

        return rate;
    }

    private readonly record struct Page(long Granule, uint Serial, int Segments, int Payload);

    // One Ogg packet read in order across however many pages carry it. A value inside it can be
    // stepped over in the file instead of buffered. Holds one page's segment table and nothing else,
    // whatever the packet's length. A page of a foreign serial belongs to another logical stream
    // multiplexed into the file and is skipped, and the packet continues past it.
    private sealed class OggPacket(FileStream stream, uint serial, long page)
    {
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
        // next page as the current one runs out. A closed packet ends what may be read even when the
        // page holds further segments, because those belong to the next packet. A segment shorter
        // than the maximum closes it.
        private int Enter()
        {
            while (_remaining == 0)
            {
                if (_closing)
                {
                    throw new FormatException(
                        "ends its Vorbis comment header before the comments it declares. The packet is truncated. Export it again.");
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
            while (true)
            {
                stream.Seek(_page, SeekOrigin.Begin);

                if (!TryReadPage(stream, _segments, out Page header))
                {
                    throw new FormatException(
                        "ends before the page continuing its Vorbis comment header. The file is truncated. Export it again.");
                }

                _page = stream.Position + header.Payload;

                if (header.Serial == serial)
                {
                    _count = header.Segments;
                    _index = 0;

                    return;
                }
            }
        }
    }
}
