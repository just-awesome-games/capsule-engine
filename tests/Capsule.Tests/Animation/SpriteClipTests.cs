using Capsule.Animation;
using Capsule.Rendering;

namespace Capsule.Tests.Animation;

public sealed class SpriteClipTests
{
    [Fact]
    public void Duration_IsTheTicksOnePassTakes()
    {
        Assert.Equal(6, SpriteAnimatorFixtures.Walk.Duration);
        Assert.Equal(5, SpriteAnimatorFixtures.Shoot.Duration);
    }

    // The sum is widened before it is checked. A total that wraps an int is refused and not read as small.
    [Fact]
    public void FramesHeldPastWhatDurationHolds_AreRefused()
    {
        Sprite frame = SpriteAnimatorFixtures.Frame(0);

        Assert.Equal(int.MaxValue, new SpriteClip([frame], [int.MaxValue]).Duration);
        Assert.Throws<ArgumentException>(() => new SpriteClip([frame, frame], [int.MaxValue, 1]));
    }
}
