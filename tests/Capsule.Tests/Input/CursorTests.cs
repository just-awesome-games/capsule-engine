using System.Numerics;
using Capsule.Assets;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

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

    // The outgoing scene stops only after the next one starts. It could not put the cursor back itself.
    [Fact]
    public void AScene_StartsWithTheDefault_WhateverTheSceneBeforeItSet()
    {
        Run run = new();
        run.Cursor.Visible = false;
        run.Cursor.Confined = true;
        run.Cursor.MakeDefault();
        Sprite arrow = new(new TextureHandle("cursors", ".png"), new TextureRegion(0, 0, 9, 9));

        using SceneSimulation menu = new(new StartHook(cursor => (cursor.Visible, cursor.Confined, cursor.Image) = (true, false, arrow)), run: run);
        (bool Visible, bool Confined, Sprite? Image) seen = default;
        using SceneSimulation next = new(new StartHook(cursor => seen = (cursor.Visible, cursor.Confined, cursor.Image)), run: run);

        Assert.Equal((false, true, null), seen);
    }

    private sealed class StartHook(Action<Cursor> onStart) : Scene
    {
        protected override void OnStart() => onStart(Run.Cursor);
    }
}
