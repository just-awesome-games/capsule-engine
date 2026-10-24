namespace Capsule.Audio;

// What the build's FLAC encoder and the host's decoder share of RFC 9639: the stream marker, the
// header codes and the checksums a frame carries. The build writes only what the host reads, and
// both stay standard FLAC, which any stock decoder opens.
internal static class FlacFormat
{
    // The STREAMINFO block's length in bytes, after its 4-byte block header.
    internal const int StreamInfoBytes = 34;

    // The frame header's sync code with its reserved bit, then the blocking-strategy bit. 0xFFF8 opens
    // a fixed-blocksize frame and 0xFFF9 a variable one.
    internal const int Sync = 0xFFF8;

    // The four bytes a FLAC stream opens with.
    internal static ReadOnlySpan<byte> Marker => "fLaC"u8;

    // The sample rates a frame header's codes 1 to 11 name. Code 0 defers to STREAMINFO.
    internal static ReadOnlySpan<int> Rates => [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000];

    // The bit depths a frame header's codes 0 to 7 name. 0 defers to STREAMINFO, and -1 is reserved.
    internal static ReadOnlySpan<int> Depths => [0, 8, 12, -1, 16, 20, 24, 32];

    // CRC-8 with polynomial x^8 + x^2 + x + 1 and initial value zero, over a frame header.
    internal static byte Crc8(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0;
        foreach (byte value in bytes)
        {
            crc = Tables.Crc8[(int)(crc ^ value)];
        }

        return (byte)crc;
    }

    // CRC-16 with polynomial x^16 + x^15 + x^2 + 1 and initial value zero, over a whole frame.
    internal static ushort Crc16(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0;
        foreach (byte value in bytes)
        {
            crc = ((crc << 8) ^ Tables.Crc16[(int)((crc >> 8) ^ value)]) & 0xFFFF;
        }

        return (ushort)crc;
    }

    private static class Tables
    {
        internal static readonly byte[] Crc8 = Build8();

        internal static readonly ushort[] Crc16 = Build16();

        private static byte[] Build8()
        {
            byte[] table = new byte[256];
            for (int i = 0; i < table.Length; i++)
            {
                uint crc = (uint)i;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1;
                }

                table[i] = (byte)crc;
            }

            return table;
        }

        private static ushort[] Build16()
        {
            ushort[] table = new ushort[256];
            for (int i = 0; i < table.Length; i++)
            {
                uint crc = (uint)i << 8;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1;
                }

                table[i] = (ushort)crc;
            }

            return table;
        }
    }
}
