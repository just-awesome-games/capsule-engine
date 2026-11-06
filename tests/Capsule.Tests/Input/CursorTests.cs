using System.Numerics;
using Capsule.Assets;
using Capsule.Input;
using Capsule.Rendering;

namespace Capsule.Tests.Input;

public sealed class CursorTests
{
    [Fact]
    public void AnImage_MustHaveItsPivotOnATexelOfItsRegion()
    {
        Cursor cursor = new();
        TextureHandle texture = new("cursors", ".png");
        TextureRegion region = new(16, 0, 9, 9);

        cursor.Image = new Sprite(texture, region, new Vector2(8.9f, 8.9f));

        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Image = new Sprite(texture, region, new Vector2(9f, 4f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Image = new Sprite(texture, region, new Vector2(4f, -0.5f)));

        cursor.Image = null;

        Assert.Null(cursor.Image);
    }

    // The operating system draws a cursor from its region's own texels, and a flip would not reach it.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AFlippedFrame_IsRefused(bool flipX, bool flipY)
    {
        Cursor cursor = new();
        Sprite frame = new(new TextureHandle("cursors", ".png"), new TextureRegion(0, 0, 9, 9), FlipX: flipX, FlipY: flipY);

        Assert.Throws<ArgumentException>(() => cursor.Image = frame);
    }
}
