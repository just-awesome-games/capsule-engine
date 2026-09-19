using System.Buffers.Binary;
using System.Text;
using Capsule.Tests.Audio;

namespace Capsule.Tests.Build;

internal static class AudioProbeFixtures
{
    internal static byte[] Malformed(string defect)
    {
        switch (defect)
        {
            case "unfinished-comment-header":
                byte[] whole = Ogg(
                    48000,
                    [0, 24000],
                    Comments(["PADDING=" + new string('x', 300), "LOOPSTART=6000"]),
                    255);

                // Cut before the comment header's second page, so the file ends on a page whose
                // lacing says the packet runs on.
                return whole[..Pages(whole)[2]];

            case "comment-overrunning-its-packet":
                return OggPairedOnAPage(48000, 24000, Overrunning("LOOPSTART=6000"));

            case "loop-past-the-end":
                return Wav(1, 16, 22050, 1764, loop: (441, 2000));

            default:
                return [];
        }
    }

    internal static byte[] Wav(
        int channels,
        int bits,
        int rate,
        int frames,
        bool padded = false,
        (uint Start, uint End)? loop = null) =>
        WavFixtures.Wav(rate, bits, channels, new byte[frames * channels * (bits / 8)], padded: padded, loop: loop);

    // One identification page, then one page per granule after the first, the second carrying the
    // comment header. commentPage laces that header across as many pages as it needs, which is what
    // an encoder writes for a header too long for one page; every page but the last then ends on a
    // maximum-length lacing value, so commentPage is a multiple of 255.
    internal static byte[] Ogg(int rate, long[] granules, byte[]? comments = null, int commentPage = 0)
    {
        using MemoryStream file = new();
        using BinaryWriter writer = new(file);

        Page(writer, granules[0], 0, Identification(rate));

        uint sequence = 1;
        for (int i = 1; i < granules.Length; i++)
        {
            byte[] payload = i == 1 ? comments ?? Comments([]) : new byte[64];

            if (i != 1 || commentPage == 0)
            {
                Page(writer, granules[i], sequence++, payload);

                continue;
            }

            for (int offset = 0; offset < payload.Length; offset += commentPage)
            {
                int run = Math.Min(commentPage, payload.Length - offset);
                Page(writer, granules[i], sequence++, payload[offset..(offset + run)], offset + run < payload.Length);
            }
        }

        writer.Flush();

        return file.ToArray();
    }

    // The identification page, then one page lacing the comment header and the setup header behind
    // it: what an encoder writes whenever both fit in a page together.
    internal static byte[] OggPairedOnAPage(int rate, long frames, byte[] comments)
    {
        byte[] setup = new byte[64];
        setup[0] = 5;
        "vorbis"u8.CopyTo(setup.AsSpan(1));

        using MemoryStream file = new();
        using BinaryWriter writer = new(file);

        Page(writer, 0, 0, Identification(rate));
        Page(writer, frames, 1, comments, setup);

        writer.Flush();

        return file.ToArray();
    }

    internal static byte[] Identification(int rate)
    {
        byte[] identification = new byte[30];
        identification[0] = 1;
        "vorbis"u8.CopyTo(identification.AsSpan(1));
        identification[11] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(identification.AsSpan(12), rate);

        return identification;
    }

    // A comment header holding one tag whose length prefix claims 64 bytes more than the packet
    // carries.
    internal static byte[] Overrunning(string tag)
    {
        byte[] header = Comments([tag]);
        int length = header.Length - Encoding.UTF8.GetByteCount(tag) - sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(length), Encoding.UTF8.GetByteCount(tag) + 64);

        return header;
    }

    // Where each page of a file begins. The payloads these fixtures carry hold no capture pattern of
    // their own, so the pattern is only ever a page.
    internal static List<int> Pages(byte[] file)
    {
        List<int> starts = [];

        for (int offset = 0; offset + 4 <= file.Length; offset++)
        {
            if (file.AsSpan(offset).StartsWith("OggS"u8))
            {
                starts.Add(offset);
            }
        }

        return starts;
    }

    // The Vorbis comment header: its packet type, the vendor string, then one length-prefixed
    // "NAME=value" per comment.
    internal static byte[] Comments(string[] tags)
    {
        using MemoryStream header = new();
        using BinaryWriter writer = new(header);

        writer.Write((byte)3);
        writer.Write("vorbis"u8);

        byte[] vendor = Encoding.UTF8.GetBytes("capsule");
        writer.Write(vendor.Length);
        writer.Write(vendor);
        writer.Write(tags.Length);

        foreach (string tag in tags)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(tag);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();

        return header.ToArray();
    }

    // continued says the packet runs on into the next page, which makes this one's lacing table
    // nothing but maximum-length values: a shorter one would close the packet here.
    internal static void Page(BinaryWriter writer, long granule, uint sequence, byte[] payload, bool continued = false)
    {
        writer.Write("OggS"u8);
        writer.Write((byte)0);
        writer.Write((byte)(sequence == 0 ? 2 : 0));
        writer.Write(granule);
        writer.Write(0x0C0FFEE1u);
        writer.Write(sequence);
        writer.Write(0u);

        int whole = payload.Length / 255;
        writer.Write((byte)(continued ? whole : whole + 1));
        for (int i = 0; i < whole; i++)
        {
            writer.Write((byte)255);
        }

        if (!continued)
        {
            writer.Write((byte)(payload.Length % 255));
        }

        writer.Write(payload);
    }

    // Two whole packets laced into one page: the first closes on its own short lacing value, and the
    // second's segments follow it in the same table.
    internal static void Page(BinaryWriter writer, long granule, uint sequence, byte[] first, byte[] second)
    {
        writer.Write("OggS"u8);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(granule);
        writer.Write(0x0C0FFEE1u);
        writer.Write(sequence);
        writer.Write(0u);

        writer.Write((byte)((first.Length / 255) + (second.Length / 255) + 2));
        Lace(writer, first);
        Lace(writer, second);

        writer.Write(first);
        writer.Write(second);
    }

    internal static void Lace(BinaryWriter writer, byte[] packet)
    {
        for (int i = 0; i < packet.Length / 255; i++)
        {
            writer.Write((byte)255);
        }

        writer.Write((byte)(packet.Length % 255));
    }
}
