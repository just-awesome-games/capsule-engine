using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

// The cursor over the listed ticks, with no device behind it: what is due, what a frame that drew
// nothing does to it, and that each tick still owes exactly one file.
public sealed class FrameCaptureTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory(nameof(FrameCaptureTests)).FullName;

    [Fact]
    public void EveryListedTick_IsTakenOnceInOrder()
    {
        FrameCapture capture = new(_directory, [2, 0, 2]);

        Assert.Equal(["frame-0.png"], TakeAll(capture, 0));
        Assert.Equal([], TakeAll(capture, 1));
        Assert.Equal(["frame-2.png"], TakeAll(capture, 2));
        Assert.Equal([], TakeAll(capture, 3));
    }

    // A frame with no surface has nothing to save, so it must consume nothing: the tick stays due.
    [Fact]
    public void ATickFallingDueWhileNotDrawable_StaysPendingAndIsTakenOnce()
    {
        FrameCapture capture = new(_directory, [0, 1]);

        Assert.False(capture.TryTakeDue(0, drawable: false, out _));
        Assert.False(capture.TryTakeDue(1, drawable: false, out _));

        Assert.Equal(["frame-0.png", "frame-1.png"], TakeAll(capture, 2));
        Assert.Equal([], TakeAll(capture, 3));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // The file names one drawable frame at completedTick owes, in the order it owes them.
    private static List<string> TakeAll(FrameCapture capture, long completedTick)
    {
        List<string> taken = [];
        while (capture.TryTakeDue(completedTick, drawable: true, out string path))
        {
            taken.Add(Path.GetFileName(path));
        }

        return taken;
    }
}
