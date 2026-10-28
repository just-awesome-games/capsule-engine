using System.Numerics;
using Capsule.Input;

namespace Capsule.Runtime.DevTools.Recordings;

// One body record as read. A Step carries the snapshot its Steps steps consumed.
internal struct RecordingEvent
{
    internal byte Tag;
    internal long Steps;
    internal DeviceSnapshot Snapshot;
    internal Vector2 Output;
    internal double RefreshRate;
    internal HostAct Act;
    internal long Tick;
    internal ulong Checksum;
}

// Decodes the body's records in order, each Step against the snapshot before it, and the End record.
internal sealed class RecordingBody(RecordingReader reader)
{
    private DeviceSnapshot _previous;

    internal RecordingEnd End { get; private set; }

    // False once the End record is read.
    internal bool Next(out RecordingEvent read)
    {
        read = default;
        read.Tag = reader.ReadByte();
        switch (read.Tag)
        {
            case RecordingFormat.Repeat:
                read.Tag = RecordingFormat.Step;
                read.Steps = (long)reader.ReadVarint();
                read.Snapshot = _previous;
                break;
            case RecordingFormat.Step:
                read.Steps = 1;
                read.Snapshot = _previous = ReadSnapshot(in _previous);
                break;
            case RecordingFormat.Output:
                read.Output = reader.ReadVector();
                break;
            case RecordingFormat.RefreshRate:
                read.RefreshRate = reader.ReadDouble();
                break;
            case RecordingFormat.Act:
                read.Act = ReadAct();
                break;
            case RecordingFormat.Checkpoint:
                read.Tick = (long)reader.ReadVarint();
                read.Checksum = reader.ReadUInt64();
                break;
            case RecordingFormat.End:
                long finalTick = (long)reader.ReadVarint();
                byte flags = reader.ReadByte();
                ulong checksum = reader.ReadUInt64();
                End = new RecordingEnd(finalTick, (flags & 1) != 0 ? checksum : null, (flags & 2) != 0);

                return false;
            default:
                throw reader.Defect($"holds record tag 0x{read.Tag:X2} at byte {reader.Position - 1}, which this Capsule does not read");
        }

        return true;
    }

    // A Step record's mask: a bit for each field of next whose bits differ from previous's. Zero lets a
    // Repeat stand for next.
    internal static int ChangeMask(in DeviceSnapshot next, in DeviceSnapshot previous)
    {
        int mask = 0;
        mask |= next.KeyBits != previous.KeyBits ? RecordingFormat.KeysBit : 0;
        mask |= next.PadBits != previous.PadBits ? RecordingFormat.PadBit : 0;
        mask |= next.MouseBits != previous.MouseBits ? RecordingFormat.MouseBit : 0;
        mask |= !SameBits(next.Pointer, previous.Pointer) ? RecordingFormat.PointerBit : 0;
        mask |= !SameBits(next.Scroll, previous.Scroll) ? RecordingFormat.ScrollBit : 0;
        mask |= next.HasWindowFocus != previous.HasWindowFocus ? RecordingFormat.FocusBit : 0;
        mask |= next.PadFamily != previous.PadFamily ? RecordingFormat.FamilyBit : 0;
        for (int slot = 0; slot < DeviceSnapshot.AxisCount; slot++)
        {
            mask |= BitConverter.SingleToUInt32Bits(next.AxisAt(slot)) != BitConverter.SingleToUInt32Bits(previous.AxisAt(slot)) ? 1 << (RecordingFormat.AxisBit + slot) : 0;
        }

        return mask;
    }

    // The fields of next that mask names, after the mask. Keys are the indices of the bits that flipped,
    // and the pad and mouse masks are what flipped.
    internal static void WriteSnapshot(ChunkedBuffer writer, int mask, in DeviceSnapshot next, in DeviceSnapshot previous)
    {
        writer.WriteVarint((ulong)mask);
        if ((mask & RecordingFormat.KeysBit) != 0)
        {
            UInt128 keys = next.KeyBits ^ previous.KeyBits;
            writer.WriteVarint((ulong)UInt128.PopCount(keys));
            while (keys != UInt128.Zero)
            {
                writer.WriteByte((byte)UInt128.TrailingZeroCount(keys));
                keys &= keys - UInt128.One;
            }
        }

        if ((mask & RecordingFormat.PadBit) != 0)
        {
            writer.WriteVarint(next.PadBits ^ previous.PadBits);
        }

        if ((mask & RecordingFormat.MouseBit) != 0)
        {
            writer.WriteVarint(next.MouseBits ^ previous.MouseBits);
        }

        if ((mask & RecordingFormat.PointerBit) != 0)
        {
            writer.WriteVector(next.Pointer);
        }

        if ((mask & RecordingFormat.ScrollBit) != 0)
        {
            writer.WriteVector(next.Scroll);
        }

        if ((mask & RecordingFormat.FamilyBit) != 0)
        {
            writer.WriteByte((byte)next.PadFamily);
        }

        for (int slot = 0; slot < DeviceSnapshot.AxisCount; slot++)
        {
            if ((mask & (1 << (RecordingFormat.AxisBit + slot))) != 0)
            {
                writer.WriteSingle(next.AxisAt(slot));
            }
        }
    }

    internal static void WriteAct(ChunkedBuffer writer, in HostAct act)
    {
        writer.WriteByte((byte)act.Kind);
        switch (act.Kind)
        {
            case HostActKind.Load:
                writer.WriteString(act.Name!);
                break;
            case HostActKind.Move:
                WritePath(writer, act.Path!);
                writer.WriteVector(act.Point);
                break;
            case HostActKind.Command:
                writer.WriteByte((byte)act.Subject);
                if (act.Subject == PanelSubject.Entity)
                {
                    WritePath(writer, act.Path!);
                }

                writer.WriteString(act.Name!);
                writer.WriteVarint((ulong)act.Occurrence);
                break;
        }
    }

    // Bit equality, which tells -0 from 0 as the simulation could.
    internal static bool SameBits(Vector2 a, Vector2 b) =>
        BitConverter.SingleToUInt32Bits(a.X) == BitConverter.SingleToUInt32Bits(b.X)
        && BitConverter.SingleToUInt32Bits(a.Y) == BitConverter.SingleToUInt32Bits(b.Y);

    private static void WritePath(ChunkedBuffer writer, int[] path)
    {
        writer.WriteVarint((ulong)path.Length);
        foreach (int index in path)
        {
            writer.WriteVarint((ulong)index);
        }
    }

    private DeviceSnapshot ReadSnapshot(in DeviceSnapshot previous)
    {
        int mask = reader.ReadCount(int.MaxValue);
        if (mask >= 1 << (RecordingFormat.AxisBit + DeviceSnapshot.AxisCount))
        {
            throw reader.Defect($"holds a step whose change mask 0x{mask:X} names a field no snapshot has, before byte {reader.Position}");
        }

        UInt128 keys = previous.KeyBits;
        if ((mask & RecordingFormat.KeysBit) != 0)
        {
            int flipped = reader.ReadCount(DeviceSnapshot.Capacity);
            for (int index = 0; index < flipped; index++)
            {
                byte bit = reader.ReadByte();
                keys ^= bit < DeviceSnapshot.Capacity ? UInt128.One << bit : throw reader.Defect($"names key {bit} at byte {reader.Position - 1}, past the keys a snapshot holds");
            }
        }

        uint pad = (mask & RecordingFormat.PadBit) != 0 ? previous.PadBits ^ (uint)reader.ReadVarint() : previous.PadBits;
        uint mouse = (mask & RecordingFormat.MouseBit) != 0 ? previous.MouseBits ^ (uint)reader.ReadVarint() : previous.MouseBits;
        Vector2 pointer = (mask & RecordingFormat.PointerBit) != 0 ? reader.ReadVector() : previous.Pointer;
        Vector2 scroll = (mask & RecordingFormat.ScrollBit) != 0 ? reader.ReadVector() : previous.Scroll;
        bool focused = (mask & RecordingFormat.FocusBit) != 0 ? !previous.HasWindowFocus : previous.HasWindowFocus;
        PadFamily family = previous.PadFamily;
        if ((mask & RecordingFormat.FamilyBit) != 0)
        {
            byte read = reader.ReadByte();
            family = read <= (byte)PadFamily.Nintendo ? (PadFamily)read : throw reader.Defect($"names pad family {read} at byte {reader.Position - 1}, which is none Capsule has");
        }

        Span<float> axes = stackalloc float[DeviceSnapshot.AxisCount];
        for (int slot = 0; slot < axes.Length; slot++)
        {
            axes[slot] = (mask & (1 << (RecordingFormat.AxisBit + slot))) != 0 ? reader.ReadSingle() : previous.AxisAt(slot);
        }

        return DeviceSnapshot.FromParts(keys, pad, mouse, pointer, scroll, axes, focused, family);
    }

    private HostAct ReadAct()
    {
        byte kind = reader.ReadByte();
        switch ((HostActKind)kind)
        {
            case HostActKind.Restart:
                return HostAct.Restart();
            case HostActKind.Exit:
                return HostAct.Exit();
            case HostActKind.Load:
                return HostAct.Load(reader.ReadString());
            case HostActKind.Move:
                int[] path = ReadPath();
                return new HostAct(HostActKind.Move, Path: path, Point: reader.ReadVector());
            case HostActKind.Command:
                byte subject = reader.ReadByte();
                if (subject > (byte)PanelSubject.Entity)
                {
                    throw reader.Defect($"names panel subject {subject} at byte {reader.Position - 1}, which the overlay has none of");
                }

                int[]? entity = subject == (byte)PanelSubject.Entity ? ReadPath() : null;
                string label = reader.ReadString();
                return new HostAct(HostActKind.Command, label, entity, Subject: (PanelSubject)subject, Occurrence: reader.ReadCount(int.MaxValue));
            default:
                throw reader.Defect($"holds host act {kind} at byte {reader.Position - 1}, which this Capsule does not read");
        }
    }

    private int[] ReadPath()
    {
        int[] path = new int[reader.ReadCount(4096)];
        for (int level = 0; level < path.Length; level++)
        {
            path[level] = reader.ReadCount(int.MaxValue);
        }

        return path;
    }
}
