using Capsule.Audio;

namespace Capsule.Runtime.Audio;

// Reads a source in order for one voice, honouring the clip's loop region: the intro once, then the
// region forever. A boundary is crossed inside the buffer it falls in, with the read continuing from
// the region's start into the same span it ended in, so a repeat is sample-exact and gapless whatever
// the buffer size.
//
// A voice that does not loop ignores the region and reads to the end of the source.
//
// The reader belongs to the voice and outlives every play of it. It is armed over a source per play
// and cleared when the voice ends, so a voice played again allocates nothing.
internal sealed class LoopedPcmReader
{
    // The end held for a read that never wraps early, with no region or no loop.
    private const long NoRegion = long.MaxValue;

    private IPcmSource? _source;
    private bool _loop;
    private long _start;
    private long _end = NoRegion;

    // Frames read since the last rewind. A region the source yields nothing from would otherwise
    // wrap forever inside one fill.
    private bool _read = true;

    private long _frame;

    // Points the reader at source, discarding wherever the previous play left it. The region arrives
    // in seconds, both bounds a whole sample count over the source's own rate, so rounding recovers
    // the frame the file named. startFrame is where the first read begins. A source handed out fresh
    // is already at frame 0, so only an offset start seeks.
    internal void Arm(IPcmSource source, in AudioLoopRegion region, bool loop, long startFrame = 0)
    {
        _source = source;
        _loop = loop;
        _frame = startFrame;
        _read = true;

        if (startFrame > 0)
        {
            source.SeekTo(startFrame);
        }

        if (loop && region.HasRegion)
        {
            _start = (long)Math.Round(region.StartSeconds * source.SampleRate);
            _end = (long)Math.Round(region.EndSeconds * source.SampleRate);
        }
        else
        {
            _start = 0;
            _end = NoRegion;
        }
    }

    // Lets go of the source this reader was armed over, so a voice back in the pool holds neither a
    // file handle nor an ended clip's samples. Reads nothing until it is armed again.
    internal void Clear()
    {
        _source = null;
        _start = 0;
        _end = NoRegion;
        _frame = 0;
    }

    // Interleaved samples written, always a whole number of frames. Zero once the source is spent with
    // nothing repeating it, and for a reader holding no source.
    internal int Read(Span<float> target)
    {
        if (_source is not { } source)
        {
            return 0;
        }

        int filled = 0;

        while (filled < target.Length)
        {
            if (_frame >= _end && !Rewind(source))
            {
                break;
            }

            Span<float> window = target[filled..];

            // Never past the region's end. The wrap happens on the next pass, in this same buffer.
            if (_end != NoRegion)
            {
                long room = (_end - _frame) * source.Channels;
                if (room < window.Length)
                {
                    window = window[..(int)room];
                }
            }

            int read = source.Read(window);
            if (read == 0)
            {
                // The source ran out, so a looping voice starts its region, or the clip, again.
                if (!Rewind(source))
                {
                    break;
                }

                continue;
            }

            _read = true;
            _frame += read / source.Channels;
            filled += read;
        }

        return filled - (filled % source.Channels);
    }

    private bool Rewind(IPcmSource source)
    {
        if (!_loop || !_read)
        {
            return false;
        }

        _read = false;
        _frame = _start;
        source.SeekTo(_start);

        return true;
    }
}
