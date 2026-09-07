using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Capsule.Input;

namespace Capsule.Runtime.Input;

// The tape file's codec, in one place so reading and writing cannot drift apart.
//
//   magic    4 bytes, "CTAP"
//   version  uint16 little-endian
//   body     one snapshot per step, DeviceSnapshot.ByteCount bytes each, in step order
//
// Every multi-byte field is little-endian, and nothing is compressed or run-length encoded: the
// body's length is the step count times the snapshot size, so a body that does not divide evenly
// is a truncated file. Internal because a tape reaches a game as a path or an InputTape, never as
// bytes it decodes itself.
internal static class InputTapeFile
{
    private const ushort Version = 1;
    private const int HeaderBytes = 4 + sizeof(ushort);

    private static ReadOnlySpan<byte> Magic => "CTAP"u8;

    internal static void Write(Stream stream, InputTape tape)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[Magic.Length..], Version);
        stream.Write(header);

        Span<byte> step = stackalloc byte[DeviceSnapshot.ByteCount];
        foreach (DeviceSnapshot snapshot in tape)
        {
            snapshot.WriteTo(step);
            stream.Write(step);
        }
    }

    internal static InputTape Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderBytes];
        if (stream.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false) < HeaderBytes)
        {
            throw new InputTapeFormatException(
                "This is no Capsule input tape: it is shorter than a tape header. Record one with WithInputRecording.");
        }

        if (!header[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InputTapeFormatException(
                "This is no Capsule input tape: it does not begin with the tape magic. Record one with WithInputRecording.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[Magic.Length..]);
        if (version != Version)
        {
            throw new InputTapeFormatException(
                $"This input tape is version {version}, and this engine reads version {Version}. Record the run again.");
        }

        List<DeviceSnapshot> steps = [];
        byte[] step = new byte[DeviceSnapshot.ByteCount];

        while (true)
        {
            int read = stream.ReadAtLeast(step, step.Length, throwOnEndOfStream: false);
            if (read == 0)
            {
                break;
            }

            if (read < step.Length)
            {
                throw new InputTapeFormatException(
                    $"This input tape is truncated: step {steps.Count} holds {read} of {DeviceSnapshot.ByteCount} bytes.");
            }

            steps.Add(DeviceSnapshot.ReadFrom(step));
        }

        return InputTape.Of(CollectionsMarshal.AsSpan(steps));
    }
}
