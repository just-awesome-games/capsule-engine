using System.Text;
using Capsule.Audio;
using Capsule.Build.Audio;
using Capsule.Tests.Documents;
using static Capsule.Tests.Build.AudioProbeFixtures;

namespace Capsule.Tests.Build;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class AudioProbeOggTests
{
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

    // LOOPSTART with LOOPLENGTH is the common pair. LOOPSTART with LOOPEND is the same authoring spelt
    // the other way, and LOOPSTART alone loops the rest of the file. Tag names are case-insensitive.
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
}
