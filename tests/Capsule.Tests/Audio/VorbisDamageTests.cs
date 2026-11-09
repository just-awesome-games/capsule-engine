using System.Buffers.Binary;
using Capsule.Runtime.Audio.Vorbis;

namespace Capsule.Tests.Audio;

// How the vendored decoder reads a damaged Ogg Vorbis stream: it loses what the damage touched and
// decodes every page after it.
public sealed class VorbisDamageTests
{
    private static readonly byte[] Whole = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Audio/Fixtures", "many-pages.ogg"));

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
}
