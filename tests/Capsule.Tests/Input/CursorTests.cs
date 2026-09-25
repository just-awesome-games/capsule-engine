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
}
