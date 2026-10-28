using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Capsule.Runtime.DevTools.Recordings;

// The recording file, little-endian throughout: the magic and version, the header, the body's tagged
// records and the End record. A varint is LEB128. A string is its UTF-8 length as a varint, then the bytes.
internal static class RecordingFormat
{
    internal const int Version = 1;

    // The body's records. A Step record is the next step's snapshot as a change mask and the changed
    // fields, and a Repeat record is a count of steps whose snapshot did not change. The events before a
    // step apply to that step. A Checkpoint follows the step it was taken after.
    internal const byte Repeat = 0x00;
    internal const byte Step = 0x01;
    internal const byte Output = 0x10;
    internal const byte RefreshRate = 0x11;
    internal const byte Act = 0x12;
    internal const byte Checkpoint = 0x13;

    // 0x14 to 0xFE are reserved for later event kinds, such as platform-service replies and typed text,
    // each added under a version bump. A reader refuses a tag it does not know.
    internal const byte End = 0xFF;

    // A Step record's mask bits. An axis bit is its slot's, from AxisBit up.
    internal const int KeysBit = 1 << 0;
    internal const int PadBit = 1 << 1;
    internal const int MouseBit = 1 << 2;
    internal const int PointerBit = 1 << 3;
    internal const int ScrollBit = 1 << 4;
    internal const int FocusBit = 1 << 5;
    internal const int FamilyBit = 1 << 6;
    internal const int AxisBit = 7;

    internal static ReadOnlySpan<byte> Magic => "CAPSREC\0"u8;
}

// The recorder's growing byte store: one chunk allocated up front, then a chunk at a time.
internal sealed class ChunkedBuffer(int chunkBytes)
{
    private readonly List<byte[]> _full = [];
    private byte[] _chunk = new byte[chunkBytes];
    private int _used;

    internal void WriteByte(byte value)
    {
        if (_used == _chunk.Length)
        {
            Grow();
        }

        _chunk[_used++] = value;
    }

    internal void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    internal void WriteUInt32(uint value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            WriteByte((byte)(value >> shift));
        }
    }

    internal void WriteUInt64(ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            WriteByte((byte)(value >> shift));
        }
    }

    internal void WriteSingle(float value) => WriteUInt32(BitConverter.SingleToUInt32Bits(value));

    internal void WriteDouble(double value) => WriteUInt64(BitConverter.DoubleToUInt64Bits(value));

    internal void WriteVector(Vector2 value)
    {
        WriteSingle(value.X);
        WriteSingle(value.Y);
    }

    // Allocates the string's bytes. Only a host act or a saved file's header writes one.
    internal void WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint((ulong)bytes.Length);
        foreach (byte unit in bytes)
        {
            WriteByte(unit);
        }
    }

    internal void CopyTo(Stream destination)
    {
        foreach (byte[] chunk in _full)
        {
            destination.Write(chunk);
        }

        destination.Write(_chunk.AsSpan(0, _used));
    }

    // The one allocation a recording step can make, and a rare one: a chunk is hours of play. Tooling
    // allocates nothing per frame while the game runs (D-capsule-091), and this growth is its exception.
    private void Grow()
    {
        _full.Add(_chunk);
        _chunk = new byte[_chunk.Length];
        _used = 0;
    }
}

// Reads a recording's bytes front to back. Every read past the end throws a format error naming the offset.
internal sealed class RecordingReader(byte[] bytes, int start, string source)
{
    internal int Position { get; private set; } = start;

    internal bool AtEnd => Position >= bytes.Length;

    internal byte ReadByte()
    {
        if (Position >= bytes.Length)
        {
            throw Truncated();
        }

        return bytes[Position++];
    }

    internal ulong ReadVarint()
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte next = ReadByte();
            value |= (ulong)(next & 0x7F) << shift;
            if (next < 0x80)
            {
                return value;
            }
        }

        throw Defect($"holds a variable-length number longer than 64 bits at byte {Position}");
    }

    internal int ReadCount(int limit)
    {
        ulong value = ReadVarint();

        return value <= (ulong)limit ? (int)value : throw Defect($"holds a count of {value} at byte {Position}, above the {limit} it allows");
    }

    internal uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));

    internal ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));

    internal float ReadSingle() => BitConverter.UInt32BitsToSingle(ReadUInt32());

    internal double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadUInt64());

    internal Vector2 ReadVector() => new(ReadSingle(), ReadSingle());

    internal string ReadString() => Encoding.UTF8.GetString(Take(ReadCount(bytes.Length)));

    internal InvalidDataException Defect(string defect) =>
        new($"The recording '{source}' {defect}. The file is damaged, or a different tool wrote it. Record the run again.");

    private ReadOnlySpan<byte> Take(int count)
    {
        if (bytes.Length - Position < count)
        {
            throw Truncated();
        }

        ReadOnlySpan<byte> taken = bytes.AsSpan(Position, count);
        Position += count;

        return taken;
    }

    private InvalidDataException Truncated() => Defect($"ends early at byte {bytes.Length}, which a write cut short");
}
