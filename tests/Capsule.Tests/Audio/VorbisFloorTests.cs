using System.Reflection;
using Capsule.Runtime.Audio.Vorbis;
using Capsule.Runtime.Audio.Vorbis.Ogg;

namespace Capsule.Tests.Audio;

// The Vorbis I limit on a floor 1 x-list is 65 entries, the two implicit posts included. A list at
// the limit decodes.
public sealed class VorbisFloorTests
{
    private const int BlockSize = 512;

    [Fact]
    public void AFloorAtTheSpecLimit_DecodesEveryPost()
    {
        Floor1 floor = new();
        floor.Init(Setup(partitions: 9, dimensions: 7), channels: 1, BlockSize, BlockSize, []);

        // Both implicit posts are read. Every other post's book is unused, which is a valid stream.
        BitWriter audio = new();
        audio.Write(1, 1);
        audio.Write(10, 8);
        audio.Write(20, 8);

        IFloorData data = floor.Unpack(Over(audio), BlockSize, channel: 0);
        float[] residue = new float[BlockSize / 2];
        Array.Fill(residue, 1f);

        floor.Apply(data, BlockSize, residue);

        Assert.All(residue, sample => Assert.True(sample > 0f));
    }

    // A floor 1 setup whose partitions all share one class with no subclass book, giving an x-list of
    // two implicit posts plus partitions times dimensions.
    private static Packet Setup(int partitions, int dimensions)
    {
        BitWriter setup = new();
        setup.Write(partitions, 5);
        for (int i = 0; i < partitions; i++)
        {
            setup.Write(0, 4);
        }

        setup.Write(dimensions - 1, 3);
        setup.Write(0, 2);
        setup.Write(0, 8);

        // Multiplier 1, then eight range bits and a distinct x for every post.
        setup.Write(0, 2);
        setup.Write(8, 4);
        for (int x = 2; x < 2 + (partitions * dimensions); x++)
        {
            setup.Write(x, 8);
        }

        return Over(setup);
    }

    // A one-part packet over the written bytes. The decoder builds its packets from an Ogg page reader.
    // This test has no stream for one and sets the bytes on the reader fields directly.
    private static Packet Over(BitWriter bits)
    {
        Packet packet = new();
        Type type = typeof(Packet);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        ((List<int>)type.GetField("_dataParts", flags)!.GetValue(packet)!).Add(0);
        type.GetField("_data", flags)!.SetValue(packet, new Memory<byte>(bits.ToArray()));

        return packet;
    }

    // Vorbis packs fields least significant bit first.
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _bit;

        internal void Write(int value, int bits)
        {
            for (int i = 0; i < bits; i++)
            {
                if (_bit == 0)
                {
                    _bytes.Add(0);
                }

                if (((value >> i) & 1) != 0)
                {
                    _bytes[^1] |= (byte)(1 << _bit);
                }

                _bit = (_bit + 1) % 8;
            }
        }

        internal byte[] ToArray() => [.. _bytes, 0, 0, 0, 0, 0, 0, 0, 0];
    }
}
