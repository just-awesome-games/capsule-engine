using Capsule.Runtime;
using static MinimalGame.Tests.HeadlessRunTests;

namespace MinimalGame.Tests;

// Each recorded playtest replays headless to the checksums the windowed run recorded.
public sealed class RecordingTests
{
    public static TheoryData<string> Recordings => new(Directory.GetFiles("Recordings", "*.recording"));

    [Theory]
    [MemberData(nameof(Recordings))]
    public void APlaytest_ReplaysWithoutDiverging(string path)
    {
        HeadlessRunResult result = Boot().RunHeadless(Recording.Load(path));

        Assert.Null(result.DivergedAt);
    }
}
