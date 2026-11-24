using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Localization;
using Capsule.Tests.Rendering;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.UI;

// A rect fits what it holds between its Size and its MaxSize, and every container above it refits on the
// next read after any change, with no step and no call from the game in between.
public sealed class FitTests
{
    private static readonly Vector2 Canvas = new(100f, 50f);

    private static readonly InputAction Act = new("fit-test");

    [Fact]
    public void ALabel_SizesItsRect_WithSizeAsTheFloorAndMaxSizeAsTheCap_AndItsContainerRefits()
    {
        BoxContainer box = new(Axis.Horizontal, Anchor.TopLeft, Vector2.Zero) { Padding = new Insets(1f) };
        ColorRect frame = new();
        box.Add(frame);
        ScreenEntity caption = new(Anchor.Left, Vector2.Zero) { Parent = box };
        Label label = new(FontFixtures.Font(), "AB");
        caption.Add(label);

        using SimulationHost run = Run(box);

        Assert.Equal(new Vector2(FontFixtures.WordWidth + 2f, FontFixtures.LineHeight + 2f), frame.Bounds.Size);

        label.Text = "AB AB";
        Assert.Equal(new Vector2(FontFixtures.TwoWordsWidth + 2f, 12f), frame.Bounds.Size);

        caption.Size = new Vector2(30f, 0f);
        Assert.Equal(new Vector2(32f, 12f), frame.Bounds.Size);

        // The floor outranks the cap on X, and the cap holds the text's height down on Y.
        caption.MaxSize = new Vector2(20f, 4f);
        Assert.Equal(new Vector2(32f, 6f), frame.Bounds.Size);

        caption.Size = Vector2.Zero;
        Assert.Equal(new Vector2(22f, 6f), frame.Bounds.Size);

        label.Visible = false;
        Assert.Equal(new Vector2(2f, 2f), frame.Bounds.Size);
    }

    // A keyed text measures nothing outside a running scene. A literal write that ends it refits, even one equal
    // to what the label showed before.
    [Fact]
    public void ALiteralWriteEndingAKeyedText_RefitsItsContainer()
    {
        BoxContainer box = new(Axis.Horizontal, Anchor.TopLeft, Vector2.Zero);
        Label label = new(FontFixtures.Font(), "AB");
        new ScreenEntity(Anchor.TopLeft, Vector2.Zero) { Parent = box }.Add(label);

        label.SetText(StringTableFixtures.Hint, "AB");
        Assert.Equal(Vector2.Zero, box.Extent);

        label.Text = "AB";
        Assert.Equal(new Vector2(FontFixtures.WordWidth, FontFixtures.LineHeight), box.Extent);
    }

    // A capped panel's padding narrows the cap its child's text wraps at. A page of a set width wraps there,
    // and a label of a negative width lays its run out unwrapped there, as it draws.
    [Fact]
    public void WrappedText_TakesTheWidthItIsGiven_AndUnderACapShrinksToItsLineOrWrapsAtTheCap()
    {
        BoxContainer panel = new(Axis.Vertical, Anchor.TopLeft, Vector2.Zero) { Padding = new Insets(1f), MaxSize = new Vector2(20f, float.PositiveInfinity) };
        ColorRect frame = new();
        panel.Add(frame);
        ScreenEntity body = new(Anchor.TopLeft, Vector2.Zero) { Parent = panel };
        Label text = new(FontFixtures.Font(), "AB") { Wrap = TextWrap.Word };
        body.Add(text);

        ScreenEntity page = new(Anchor.TopLeft, new Vector2(0f, 30f)) { Size = new Vector2(12f, 0f) };
        ColorRect sheet = new();
        page.Add(sheet);
        page.Add(new Label(FontFixtures.Font(), "AB AB") { Wrap = TextWrap.Word });

        ScreenEntity strip = new(Anchor.TopLeft, new Vector2(40f, 30f)) { Size = new Vector2(12f, 0f) };
        strip.Add(new Label(FontFixtures.Font(), "AB AB") { Wrap = TextWrap.Word, Size = new Vector2(-1f, 0f) });

        using SimulationHost run = Run(panel, page, strip);

        Assert.Equal(new Vector2(FontFixtures.WordWidth + 2f, FontFixtures.LineHeight + 2f), frame.Bounds.Size);
        Assert.Equal(new Vector2(12f, 2f * FontFixtures.LineHeight), sheet.Bounds.Size);
        Assert.Equal(new Vector2(FontFixtures.TwoWordsWidth, FontFixtures.LineHeight), strip.Extent);

        text.Text = "AB AB";
        Assert.Equal(new Vector2(20f, (2f * FontFixtures.LineHeight) + 2f), frame.Bounds.Size);
    }

    // A sprite draws by its own pivot, and a renderer given no size fills the rect rather than sizing it.
    [Fact]
    public void ASpriteOrAnUnsizedFill_GivesItsRectNoExtent()
    {
        ScreenEntity holder = new(Anchor.Center, Vector2.Zero);
        holder.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)));
        holder.Add(new ColorRect());
        holder.Add(new NineSlice(SceneFixtures.Frame(8, 8), new SliceInsets(2, 2, 2, 2)));

        using SimulationHost run = Run(holder);

        Assert.Equal(Vector2.Zero, holder.Extent);
    }

    // A button's name changes with the device and no write to the label, and the container still refits.
    [Fact]
    public void ALabelNamingAButton_RefitsItsContainer_WhenTheDeviceChangesItsName()
    {
        Run game = new() { Canvas = Canvas };
        game.Input.Bind(Act, Key.Space, PadButton.South);
        BoxContainer box = new(Axis.Horizontal, Anchor.TopLeft, Vector2.Zero);
        ColorRect frame = new();
        box.Add(frame);
        Label label = new(BitmapFont.Default);
        label.SetText(Act);
        new ScreenEntity(Anchor.TopLeft, Vector2.Zero) { Parent = box }.Add(label);

        using SimulationHost run = SceneFixtures.Simulate(game, box);

        run.Step(DeviceSnapshot.Of(Key.Space));
        float typed = frame.Bounds.Size.X;
        run.Step(DeviceSnapshot.Empty.With(PadButton.South));

        Assert.Equal(label.ContentSize.X, frame.Bounds.Size.X);
        Assert.True(frame.Bounds.Size.X < typed, $"The pad's name is {frame.Bounds.Size.X} wide and the key's {typed}.");
    }

    // A scroll container passes a change below it up to the container it sits in.
    [Fact]
    public void AScrollContainer_GrowsToItsContentUpToItsMaxSize_AndScrollsOnlyPastIt()
    {
        BoxContainer outer = new(Axis.Vertical, Anchor.TopLeft, Vector2.Zero);
        ColorRect frame = new();
        outer.Add(frame);
        ScrollContainer list = new(Anchor.TopLeft, Vector2.Zero) { Parent = outer, MaxSize = new Vector2(float.PositiveInfinity, 20f) };
        BoxContainer column = new(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = list };
        ScreenEntity row = new(Anchor.TopLeft, Vector2.Zero) { Parent = column, Size = new Vector2(30f, 15f) };

        using SimulationHost run = Run(outer);

        Assert.Equal((new Vector2(30f, 15f), Vector2.Zero), (frame.Bounds.Size, list.MaxScrollOffset));

        row.Size = new Vector2(40f, 50f);

        Assert.Equal((new Vector2(40f, 20f), new Vector2(0f, 30f)), (frame.Bounds.Size, list.MaxScrollOffset));
    }

    private static SimulationHost Run(params ReadOnlySpan<Entity> roots) => SceneFixtures.Simulate(new Run { Canvas = Canvas }, roots);
}
