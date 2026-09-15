using System.Buffers.Binary;

namespace Capsule.Tests.Audio;

// One RIFF/WAVE file: the 'fmt ' chunk its arguments describe, whatever extra chunks a case needs
// ahead of the data, and the sample bytes it carries.
internal static class WavFixtures
{
    /// <summary>
    /// A WAV file over <paramref name="samples"/>, whose length is the data chunk's.
    /// </summary>
    /// <param name="formatTag">1 for PCM, 3 for IEEE float, anything else for a format Capsule refuses.</param>
    /// <param name="padded">Whether an odd-sized chunk the walk must step over precedes the data.</param>
    /// <param name="loop">The sampler chunk's one loop, in samples, or none.</param>
    internal static byte[] Wav(
        int rate,
        int bits,
        int channels,
        byte[] samples,
        int formatTag = 1,
        bool padded = false,
        (uint Start, uint End)? loop = null)
    {
        int blockAlign = channels * (bits / 8);

        using MemoryStream file = new();
        using BinaryWriter writer = new(file);

        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)formatTag);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write((short)bits);

        if (padded)
        {
            // An odd-sized chunk the walk must step over its pad byte to leave.
            writer.Write("LIST"u8);
            writer.Write(5);
            writer.Write("INFOx"u8);
            writer.Write((byte)0);
        }

        if (loop is { } region)
        {
            // 36 fixed bytes, the last of which count the loops, then 24 bytes for the one loop.
            writer.Write("smpl"u8);
            writer.Write(36 + 24);
            writer.Write(new byte[28]);
            writer.Write(1);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(region.Start);
            writer.Write(region.End);
            writer.Write(0);
            writer.Write(0);
        }

        writer.Write("data"u8);
        writer.Write(samples.Length);
        writer.Write(samples);

        writer.Flush();
        byte[] bytes = file.ToArray();

        // The RIFF size covers everything after it.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);

        return bytes;
    }
}
