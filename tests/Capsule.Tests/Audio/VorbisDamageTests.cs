using System.Buffers.Binary;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Audio;

// How the vendored decoder reads an Ogg file that is not one clean Vorbis stream. It loses what damage
// touched and decodes every page after it, and it reads only the file's first logical stream.
public sealed class VorbisDamageTests
{
    private static readonly byte[] Whole = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures", "many-pages.ogg"));

    private const long LoopFrames = 176_400;

    private static readonly byte[] Loop = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures", "loop.ogg"));

    private static readonly int[] Pages = PageOffsets(Whole);

    private static readonly long WholeFrames = DecodeFrames(Whole);

    // Bytes that belong to no page are skipped, and the page after them decodes whole.
    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    public void JunkBeforeAPage_LosesNothing(int page)
    {
        byte[] damaged = [.. Whole[..Pages[page]], 0x55, 0x55, 0x55, 0x55, 0x55, .. Whole[Pages[page]..]];

        Assert.Equal(WholeFrames, DecodeFrames(damaged));
    }

    // A page whose checksum fails is lost, with at most the next page's samples that need it. A page
    // header planted in the damaged page's body fails its own checksum and is searched past too.
    [Theory]
    [InlineData(4, false)]
    [InlineData(20, false)]
    [InlineData(4, true)]
    [InlineData(20, true)]
    public void APageFailingItsChecksum_LosesNoMoreThanItAndTheNextPage(int page, bool plantedHeader)
    {
        byte[] damaged = (byte[])Whole.Clone();
        if (plantedHeader)
        {
            int at = Pages[page] + 100;
            Whole.AsSpan(Pages[page], 27).CopyTo(damaged.AsSpan(at));
            damaged[at + 26] = 1;
            damaged[at + 27] = 50;
        }
        else
        {
            damaged[Pages[page] + 200] ^= 0xFF;
        }

        long lost = WholeFrames - DecodeFrames(damaged);

        Assert.InRange(lost, 1, Granule(page + 1) - Granule(page - 1));
    }

    // A packet naming a mode the setup lacks is skipped as one that is not audio is, both when it is
    // read and when a seek counts the samples on its page back past it.
    [Fact]
    public void APacketNamingAModeTheSetupLacks_IsSkippedLikeOneThatIsNotAudio()
    {
        float[] notAudio = DecodeWithSeek(ThreeModeStream([0x01]), 200);

        Assert.NotEmpty(notAudio);
        Assert.Equal(notAudio, DecodeWithSeek(ThreeModeStream(Audio(mode: 3)), 200));
    }

    // A second stream chained after the first or interleaved with it page by page is ignored, and so is
    // the first chained after itself under the same serial. The first ends where its own last page
    // says, and a seek finds its pages among the other's.
    [Theory]
    [InlineData("many-pages.ogg", false)]
    [InlineData("many-pages.ogg", true)]
    [InlineData("loop.ogg", false)]
    public void AFileOfTwoStreams_DecodesAndSeeksTheFirstAlone(string second, bool interleaved)
    {
        byte[] other = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures", second));
        byte[][] loopPages = Split(Loop);
        byte[][] otherPages = Split(other);
        byte[] file = interleaved
            ? [.. Enumerable.Range(0, Math.Max(loopPages.Length, otherPages.Length)).SelectMany(page => (loopPages.ElementAtOrDefault(page) ?? []).Concat(otherPages.ElementAtOrDefault(page) ?? []))]
            : [.. Loop, .. other];

        Assert.Equal(DecodeWithSeek(Loop, 100_000), DecodeWithSeek(file, 100_000));
    }

    // A last page missing its end-of-stream flag still ends the stream at its granule position, and
    // the stream decodes and seeks as the flagged one does. A seek into the last packet after a read
    // to the end lands on its target too. A decoder that stops advancing spins inside one read, and the
    // decodes run on a worker so that fails the test in place of stalling the run.
    [Theory]
    [InlineData(100_000)]
    [InlineData(176_000)]
    public async Task ALastPageWithoutItsEndFlag_DecodesAndSeeksAsAFlaggedOne(long seekFrame)
    {
        Task<(float[] Flagged, float[] Unflagged)> decodes = Task.Run(() => (DecodeWithSeek(Loop, seekFrame), DecodeWithSeek(Unflagged(Loop), seekFrame)));
        Assert.True(await Task.WhenAny(decodes, Task.Delay(TimeSpan.FromSeconds(10))) == decodes, "the decode after the seek stopped advancing");
        (float[] flagged, float[] unflagged) = await decodes;

        Assert.Equal((2 * LoopFrames) - seekFrame, flagged.Length);
        Assert.Equal(flagged, unflagged);
    }

    // A last page whose granule falls before its final packet's first sample cuts that packet whole, flagged
    // or not, and the decode ends there in place of spinning.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALastGranuleBeforeItsFinalPacket_EndsTheDecode(bool flagged)
    {
        byte[] file = [.. Loop];
        int[] pages = PageOffsets(file);
        long before = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(pages[^2] + 6)) + 1;
        Span<byte> last = file.AsSpan(pages[^1]);
        BinaryPrimitives.WriteInt64LittleEndian(last[6..], before);
        if (!flagged)
        {
            last[5] &= 0b1111_1011;
        }

        Seal(last);

        Task<long> decode = Task.Run(() => DecodeFrames(file));
        Assert.True(await Task.WhenAny(decode, Task.Delay(TimeSpan.FromSeconds(10))) == decode, "the decode stopped advancing");
        Assert.InRange(await decode, 0, LoopFrames - 1);
    }

    // A page with no packets ends the first stream, as the empty last page some encoders write after
    // an unflagged one does, and the same stream chained after it under its serial is ignored.
    [Fact]
    public void AnEmptyPage_EndsTheFirstStream()
    {
        Span<byte> last = Loop.AsSpan(PageOffsets(Loop)[^1]);
        byte[] empty = Page(BinaryPrimitives.ReadInt32LittleEndian(last[14..]), BinaryPrimitives.ReadInt32LittleEndian(last[18..]) + 1, 0x04, BinaryPrimitives.ReadInt64LittleEndian(last[6..]));

        Assert.Equal(DecodeWithSeek(Loop, 100_000), DecodeWithSeek([.. Unflagged(Loop), .. empty, .. Loop], 100_000));
    }

    // Where each page starts, read from each page's header.
    private static int[] PageOffsets(byte[] file)
    {
        List<int> offsets = [];
        for (int at = 0; at < file.Length;)
        {
            offsets.Add(at);
            int segments = file[at + 26];
            int body = 0;
            for (int i = 0; i < segments; i++)
            {
                body += file[at + 27 + i];
            }

            at += 27 + segments + body;
        }

        return [.. offsets];
    }

    // The file with its last page's end-of-stream flag cleared.
    private static byte[] Unflagged(byte[] file)
    {
        byte[] copy = [.. file];
        Span<byte> last = copy.AsSpan(PageOffsets(copy)[^1]);
        last[5] &= 0b1111_1011;
        Seal(last);

        return copy;
    }

    // The file's pages, each whole.
    private static byte[][] Split(byte[] file)
    {
        int[] starts = [.. PageOffsets(file), file.Length];

        return [.. starts[..^1].Zip(starts[1..], (start, end) => file[start..end])];
    }

    // The frames decoded once the page's last complete packet is.
    private static long Granule(int page) => BinaryPrimitives.ReadInt64LittleEndian(Whole.AsSpan(Pages[page] + 6));

    private static long DecodeFrames(byte[] file)
    {
        using VorbisReader reader = new(new MemoryStream(file));
        float[] scratch = new float[reader.Channels * 4096];
        long frames = 0;
        for (int read; (read = reader.ReadSamples(scratch)) > 0;)
        {
            frames += read / reader.Channels;
        }

        return frames;
    }

    // Every sample decoded from the start, then every sample from the frame a seek lands on.
    private static float[] DecodeWithSeek(byte[] file, long seekFrame)
    {
        using VorbisReader reader = new(new MemoryStream(file));
        float[] scratch = new float[reader.Channels * 4096];
        List<float> samples = [];
        ReadToEnd();
        reader.SeekTo(seekFrame);
        ReadToEnd();

        return [.. samples];

        void ReadToEnd()
        {
            for (int read; (read = reader.ReadSamples(scratch)) > 0;)
            {
                samples.AddRange(scratch[..read]);
            }
        }
    }

    // A mono stream of 256-frame blocks whose setup has three modes. A packet names its mode in two bits
    // and can name a fourth. The six audio packets decode silent, and the third is the one given.
    private static byte[] ThreeModeStream(byte[] third)
    {
        byte[] identification = [0x01, .. "vorbis"u8, 0, 0, 0, 0, 1, .. BitConverter.GetBytes(48_000), .. new byte[12], 0x88, 1];
        byte[] comment = [0x03, .. "vorbis"u8, .. new byte[8], 1];

        VorbisBitWriter setup = new();
        setup.Write([0x05, .. "vorbis"u8]);

        // One codebook of one dimension and two one-bit entries, with no lookup table.
        setup.Write(0, 8);
        setup.Write(0x564342, 24);
        setup.Write(1, 16);
        setup.Write(2, 24);
        setup.Write(0, 2 + 5 + 5 + 4);

        // The one placeholder time transform, then a floor 1 with no partitions and eight range bits.
        setup.Write(0, 6 + 16 + 6);
        setup.Write(1, 16);
        setup.Write(0, 5 + 2);
        setup.Write(8, 4);

        // A residue 0 that covers nothing, classified by the codebook, then a mapping of both.
        setup.Write(0, 6 + 16);
        setup.Write(0, 24 + 24 + 24);
        setup.Write(0, 6 + 8 + 4);
        setup.Write(0, 6 + 16 + 4 + 8 + 8 + 8);

        // Three modes of short blocks on the mapping, then the framing bit.
        setup.Write(2, 6);
        for (int mode = 0; mode < 3; mode++)
        {
            setup.Write(0, 1 + 16);
            setup.Write(0, 16 + 8);
        }

        setup.Write(1, 1);

        byte[] silent = Audio(mode: 0);

        return [.. Page(1, 0, 0x02, 0, identification), .. Page(1, 1, 0, 0, comment, setup.ToArray()), .. Page(1, 2, 0x04, 5 * 128, silent, silent, third, silent, silent, silent)];
    }

    // An audio packet of the three-mode stream naming the mode, with its floor unused.
    private static byte[] Audio(int mode)
    {
        VorbisBitWriter bits = new();
        bits.Write(0, 1);
        bits.Write(mode, 2);
        bits.Write(0, 1);

        return bits.ToArray();
    }

    // A page of whole packets, each shorter than a lacing value's 255 bytes, with its checksum set.
    private static byte[] Page(int serial, int sequence, byte flags, long granule, params byte[][] packets)
    {
        byte[] page = [.. "OggS"u8, 0, flags, .. BitConverter.GetBytes(granule), .. BitConverter.GetBytes(serial), .. BitConverter.GetBytes(sequence), 0, 0, 0, 0, (byte)packets.Length, .. packets.Select(static packet => (byte)packet.Length), .. packets.SelectMany(static packet => packet)];
        Seal(page);

        return page;
    }

    // Sets a page's checksum over its bytes with the checksum field zeroed.
    private static void Seal(Span<byte> page)
    {
        page.Slice(22, 4).Clear();
        uint crc = 0;
        foreach (byte value in page)
        {
            crc ^= (uint)value << 24;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000_0000) != 0 ? (crc << 1) ^ 0x04C1_1DB7 : crc << 1;
            }
        }

        BinaryPrimitives.WriteUInt32LittleEndian(page[22..], crc);
    }
}
