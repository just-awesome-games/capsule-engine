using System.Buffers.Binary;
using System.Text;
using Capsule.Audio;
using Capsule.Build;
using Capsule.Build.Audio;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Audio;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class AudioProbeTests
{
    [Theory]
    [InlineData(1, 16, 22050, 1764, 0.08)]
    [InlineData(2, 16, 44100, 22050, 0.5)]
    [InlineData(1, 8, 8000, 4000, 0.5)]
    public void AWavsDuration_IsItsDataChunkOverItsFrameSizeAndRate(
        int channels,
        int bits,
        int rate,
        int frames,
        double expected)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(channels, bits, rate, frames));

        AudioProbe.Measurement measured = AudioProbe.Measure("clip.wav");

        Assert.Equal(expected, measured.DurationSeconds, 9);
        Assert.Equal(AudioLoopRegion.None, measured.Loop);
    }

    // The chunk walk skips whatever it does not read, whether it precedes 'data' or follows it.
    [Fact]
    public void AWavCarryingOtherChunks_IsMeasuredFromTheOnesThatMatter()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(1, 16, 22050, 1764, padded: true));

        Assert.Equal(0.08, AudioProbe.Measure("clip.wav").DurationSeconds, 9);
    }

    // A sampler chunk's loop end is the last sample sounded, so the region ends one past it.
    [Fact]
    public void AWavsLoopRegion_IsTheFirstSampleLoopOfItsSamplerChunk()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.wav", Wav(1, 16, 22050, 1764, loop: (441, 881)));

        AudioLoopRegion region = AudioProbe.Measure("clip.wav").Loop;

        Assert.Equal(441 / 22050.0, region.StartSeconds, 12);
        Assert.Equal(882 / 22050.0, region.EndSeconds, 12);
    }

    // The last page carrying a granule position is the stream's frame count; a page that completes
    // no packet carries -1 and says nothing about the length.
    [Fact]
    public void AnOggsDuration_IsItsLastGranulePositionOverItsVorbisRate()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.ogg", Ogg(48000, [0, 24000, -1]));

        AudioProbe.Measurement measured = AudioProbe.Measure("clip.ogg");

        Assert.Equal(0.5, measured.DurationSeconds, 9);
        Assert.Equal(AudioLoopRegion.None, measured.Loop);
    }

    // LOOPSTART with LOOPLENGTH is the RPG Maker pair; LOOPSTART with LOOPEND is the same authoring
    // spelt the other way; LOOPSTART alone loops the rest of the file. Tag names are case-insensitive.
    [Theory]
    [InlineData(new[] { "LOOPSTART=6000", "LOOPLENGTH=12000" }, 18000)]
    [InlineData(new[] { "LOOPSTART=6000", "LOOPEND=18000" }, 18000)]
    [InlineData(new[] { "LoopStart=6000" }, 24000)]
    public void AnOggsLoopRegion_IsItsLoopComments(string[] tags, int end)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.ogg", Ogg(48000, [0, 24000], Comments(tags)));

        AudioLoopRegion region = AudioProbe.Measure("clip.ogg").Loop;

        Assert.Equal(0.125, region.StartSeconds, 12);
        Assert.Equal(end / 48000.0, region.EndSeconds, 12);
    }

    // The gathered-pages read stopped at 64 KiB, so a picture block ahead of the pair hid it and one
    // between its halves turned a region into a loop to the clip's end. The packet is walked whole
    // and a value no tag can be is stepped over in the file, so neither position hides anything.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnOggTaggingALoopAroundAHugeComment_IsMeasuredFromTheWholeCommentPacket(int position)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        List<string> tags = ["LOOPSTART=6000", "LOOPLENGTH=12000"];
        tags.Insert(position, "METADATA_BLOCK_PICTURE=" + new string('P', 80 * 1024));

        File.WriteAllBytes("clip.ogg", Ogg(48000, [0, 24000], Comments([.. tags]), 255 * 255));

        AudioLoopRegion region = AudioProbe.Measure("clip.ogg").Loop;

        Assert.Equal(0.125, region.StartSeconds, 12);
        Assert.Equal(18000 / 48000.0, region.EndSeconds, 12);
    }

    // The padding is sized so the first page ends inside "LOOPLENGTH=12000": a tag is read across the
    // page boundary it straddles rather than lost with the half of it on either side.
    [Fact]
    public void AnOggWhoseLoopTagsStraddleAPageBoundary_IsMeasuredFromTheWholeCommentPacket()
    {
        const int PageBytes = 255;

        using SceneDocumentFixtures.Workspace workspace = new();
        byte[] header = Comments(["PADDING=" + new string('x', 195), "LOOPSTART=6000", "LOOPLENGTH=12000"]);

        int straddling = header.AsSpan().IndexOf("LOOPLENGTH"u8);
        Assert.InRange(straddling, 0, PageBytes - 1);
        Assert.InRange(straddling + "LOOPLENGTH=12000".Length, PageBytes + 1, header.Length);

        File.WriteAllBytes("clip.ogg", Ogg(48000, [0, 24000], header, PageBytes));

        AudioLoopRegion region = AudioProbe.Measure("clip.ogg").Loop;

        Assert.Equal(0.125, region.StartSeconds, 12);
        Assert.Equal(18000 / 48000.0, region.EndSeconds, 12);
    }

    // A comment header the file does not finish is a malformed container, not a clip without a
    // region: reading the tags that happen to precede the cut would ship a loop nobody authored.
    [Fact]
    public void AnOggWhoseCommentHeaderTheFileDoesNotFinish_FailsTheBuildNamingTheFile()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("music");

        byte[] whole = Ogg(
            48000,
            [0, 24000],
            Comments(["PADDING=" + new string('x', 300), "LOOPSTART=6000"]),
            255);

        // Cut before the comment header's second page, so the file ends on a page whose lacing says
        // the packet runs on.
        File.WriteAllBytes("music/theme.ogg", whole[..Pages(whole)[2]]);

        StringWriter error = new();
        int exitCode = AudioTool.Emit(
            [new DocumentSource("music/theme", "music/theme.ogg")],
            "CapsuleAssets.Audio.g.cs",
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("music/theme.ogg:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("truncated", error.ToString(), StringComparison.Ordinal);
    }

    // A packet closes on the first segment shorter than the maximum, wherever in the page that
    // falls. A comment declaring more bytes than its own packet holds is a malformed header, not
    // licence to read the setup packet laced behind it on the same page as tag text.
    [Fact]
    public void AnOggWhoseCommentOverrunsItsOwnPacket_FailsTheBuildNamingTheFile()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("music");
        File.WriteAllBytes("music/theme.ogg", OggPairedOnAPage(48000, 24000, Overrunning("LOOPSTART=6000")));

        StringWriter error = new();
        int exitCode = AudioTool.Emit(
            [new DocumentSource("music/theme", "music/theme.ogg")],
            "CapsuleAssets.Audio.g.cs",
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("music/theme.ogg:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("truncated", error.ToString(), StringComparison.Ordinal);
    }

    // The same lacing, well formed: stopping at the comment packet's end must not cost the tags in
    // front of it.
    [Fact]
    public void AnOggWhoseSetupHeaderSharesTheCommentPage_IsMeasuredFromItsCommentsAlone()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes(
            "clip.ogg",
            OggPairedOnAPage(48000, 24000, Comments(["LOOPSTART=6000", "LOOPLENGTH=12000"])));

        AudioLoopRegion region = AudioProbe.Measure("clip.ogg").Loop;

        Assert.Equal(0.125, region.StartSeconds, 12);
        Assert.Equal(18000 / 48000.0, region.EndSeconds, 12);
    }

    [Fact]
    public void AnOggTaggingALoopThatIsNoSampleCount_IsRefused()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes("clip.ogg", Ogg(48000, [0, 24000], Comments(["LOOPSTART=one bar"])));

        AudioFormatException refused = Assert.Throws<AudioFormatException>(() => AudioProbe.Measure("clip.ogg"));

        Assert.Contains("LOOPSTART=\"one bar\"", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(6000, 6000)]
    [InlineData(18000, 6000)]
    [InlineData(6000, 30000)]
    public void ARegionItsOwnClipCannotHold_IsRefused(int start, int end)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        File.WriteAllBytes(
            "clip.ogg",
            Ogg(48000, [0, 24000], Comments([$"LOOPSTART={start}", $"LOOPEND={end}"])));

        AudioFormatException refused = Assert.Throws<AudioFormatException>(() => AudioProbe.Measure("clip.ogg"));

        Assert.Contains($"[{start}, {end})", refused.Message, StringComparison.Ordinal);
        Assert.Contains("24000 sample(s)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWavLoopingPastItsOwnEnd_FailsTheBuildNamingTheFileAndTheRegion()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("music");
        File.WriteAllBytes("music/theme.wav", Wav(1, 16, 22050, 1764, loop: (441, 2000)));

        StringWriter error = new();
        int exitCode = AudioTool.Emit(
            [new DocumentSource("music/theme", "music/theme.wav")],
            "CapsuleAssets.Audio.g.cs",
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("music/theme.wav:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("[441, 2001) in 1764 sample(s)", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOggOpeningWithNoVorbisHeader_IsRefused()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        byte[] file = Ogg(48000, [24000]);

        // The packet type, at the head of the first page's payload: 1 is the identification header.
        file[28] = 5;
        File.WriteAllBytes("clip.ogg", file);

        AudioFormatException refused =
            Assert.Throws<AudioFormatException>(() => AudioProbe.Measure("clip.ogg"));

        Assert.Contains("Vorbis", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWavCapsuleCannotDecode_IsRefused()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        byte[] file = Wav(1, 16, 22050, 8);

        // The format tag, at the head of the 'fmt ' chunk: 0x11 is IMA ADPCM.
        file[20] = 0x11;
        File.WriteAllBytes("clip.wav", file);

        AudioFormatException refused =
            Assert.Throws<AudioFormatException>(() => AudioProbe.Measure("clip.wav"));

        Assert.Contains("PCM", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedSource_FailsTheBuildNamingTheFileAndTheDefect()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("steps");
        File.WriteAllBytes("steps/stone.wav", []);
        File.WriteAllBytes("good.wav", Wav(1, 16, 22050, 1764));

        StringWriter error = new();
        int exitCode = AudioTool.Emit(
            [new DocumentSource("steps/stone", "steps/stone.wav"), new DocumentSource("good", "good.wav")],
            "CapsuleAssets.Audio.g.cs",
            TextWriter.Null,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("steps/stone.wav:", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("truncated", error.ToString(), StringComparison.Ordinal);

        // Nothing is written where a source failed: a game never compiles against half a registry.
        Assert.False(File.Exists("CapsuleAssets.Audio.g.cs"));
    }

    private static byte[] Wav(
        int channels,
        int bits,
        int rate,
        int frames,
        bool padded = false,
        (uint Start, uint End)? loop = null)
    {
        int blockAlign = channels * (bits / 8);
        int dataBytes = frames * blockAlign;

        using MemoryStream file = new();
        using BinaryWriter writer = new(file);

        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
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
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);

        writer.Flush();
        byte[] bytes = file.ToArray();

        // The RIFF size covers everything after it.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);

        return bytes;
    }

    // One identification page, then one page per granule after the first, the second carrying the
    // comment header. commentPage laces that header across as many pages as it needs, which is what
    // an encoder writes for a header too long for one page; every page but the last then ends on a
    // maximum-length lacing value, so commentPage is a multiple of 255.
    private static byte[] Ogg(int rate, long[] granules, byte[]? comments = null, int commentPage = 0)
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
    private static byte[] OggPairedOnAPage(int rate, long frames, byte[] comments)
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

    private static byte[] Identification(int rate)
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
    private static byte[] Overrunning(string tag)
    {
        byte[] header = Comments([tag]);
        int length = header.Length - Encoding.UTF8.GetByteCount(tag) - sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(length), Encoding.UTF8.GetByteCount(tag) + 64);

        return header;
    }

    // Where each page of a file begins. The payloads these fixtures carry hold no capture pattern of
    // their own, so the pattern is only ever a page.
    private static List<int> Pages(byte[] file)
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
    private static byte[] Comments(string[] tags)
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
    private static void Page(BinaryWriter writer, long granule, uint sequence, byte[] payload, bool continued = false)
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
    private static void Page(BinaryWriter writer, long granule, uint sequence, byte[] first, byte[] second)
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

    private static void Lace(BinaryWriter writer, byte[] packet)
    {
        for (int i = 0; i < packet.Length / 255; i++)
        {
            writer.Write((byte)255);
        }

        writer.Write((byte)(packet.Length % 255));
    }
}
