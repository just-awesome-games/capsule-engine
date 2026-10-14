using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

// A clip is what the pointer sees as well as what the eye does. The directions ignore it, which is how a
// pad reaches a row the clip has scrolled out of view.
public sealed class ClipRectTests
{
    // The strip the clip shows is (0, 40) to (20, 50). The clipped item spans (0, 48) to (20, 58). Only
    // its top two pixels show.
    private static readonly Vector2 InStrip = new(10f, 49f);

    private static readonly Vector2 PastStrip = new(10f, 55f);

    [Fact]
    public void ThePointer_NeitherHoversNorClicksAnItemOutsideItsClip_WhileADirectionStillReachesIt()
    {
        ClippedColumn column = new();
        using Menu menu = column.Menu.Open();

        menu.Pointer(PastStrip).Rest();
        Assert.Equal(0, menu.FocusedIndex);

        menu.Tap(MouseButton.Left);
        Assert.Empty(menu.Log);

        menu.Tap(Key.Down);
        Assert.Equal(1, menu.FocusedIndex);

        menu.Tap(Key.Up);
        menu.Pointer(InStrip).Rest();
        Assert.Equal(1, menu.FocusedIndex);

        menu.Tap(MouseButton.Left);
        Assert.Equal("pressed 1", menu.Log[^1]);
    }

    [Fact]
    public void TheWheel_AdjustsTheFocusedItemOnlyWhereItsClipShowsThePointer()
    {
        ClippedColumn column = new();
        column.Item.Adjusts = Axis.Vertical;
        using Menu menu = column.Menu.Open();
        menu.FocusOn(1);

        menu.Pointer(PastStrip).Wheel(1f);
        Assert.Empty(menu.Log);

        menu.Pointer(InStrip).Wheel(1f);
        Assert.Equal(["adjusted 1 1"], menu.Log);
    }

    // The clip above an item is read with no draw. It must follow the clip leaving and rejoining and the
    // item moving out from under it.
    [Fact]
    public void TheClipAboveAnItem_FollowsTheClipBeingRemovedAddedAndTheItemReparented()
    {
        ClippedColumn column = new();
        using Menu menu = column.Menu.Open();

        menu.Pointer(PastStrip).Rest();
        Assert.Equal(0, menu.FocusedIndex);

        column.Frame.Remove(column.Clip);
        menu.Pointer(InNeither).Rest().Pointer(PastStrip).Rest();
        Assert.Equal(1, menu.FocusedIndex);

        column.Frame.Add(column.Clip);
        menu.Tap(Key.Up).Pointer(InNeither).Rest().Pointer(PastStrip).Rest();
        Assert.Equal(0, menu.FocusedIndex);

        column.Row.Parent = column.Bare;
        menu.Pointer(InNeither).Rest().Pointer(PastStrip).Rest();
        Assert.Equal(1, menu.FocusedIndex);
    }

    [Fact]
    public void AClipRect_RefusesATurnInItsAncestryButNotOneBeneathIt()
    {
        ScreenEntity parent = new(Anchor.TopLeft, Vector2.Zero) { Size = new Vector2(50f, 50f) };
        ScreenEntity frame = new(Anchor.TopLeft, Vector2.Zero) { Parent = parent, Size = new Vector2(20f, 20f) };
        ScreenEntity row = new(Anchor.TopLeft, Vector2.Zero) { Parent = frame, Size = new Vector2(20f, 10f) };
        frame.Add(new ClipRect());

        row.Rotation = 1f;

        Assert.Throws<InvalidOperationException>(() => frame.Rotation = 1f);
        Assert.Throws<InvalidOperationException>(() => parent.Rotation = 1f);
    }

    [Fact]
    public void AClipRect_RefusesAnEntityThatIsNotOnTheScreenLayer()
    {
        Entity world = new SceneFixtures.Drifter(Vector2.Zero);
        ClipRect clip = new();

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => world.Add(clip));

        Assert.Contains("Attach it to a ScreenEntity", refused.Message, StringComparison.Ordinal);
        Assert.Null(clip.Entity);
    }

    // An unclipped first item above a panel whose clipped strip holds a second item, most of which hangs
    // below the strip. Bare is a sibling of the strip with the same rect and no clip.
    private sealed class ClippedColumn
    {
        internal ClipRect Clip { get; } = new();

        internal ScreenEntity Frame { get; }

        internal ScreenEntity Bare { get; }

        internal ScreenEntity Row { get; }

        internal Focusable Item { get; }

        internal Menu Menu { get; }

        internal ClippedColumn()
        {
            ScreenEntity panel = new(Anchor.TopLeft, new Vector2(0f, 40f)) { Size = Box };
            Frame = new ScreenEntity(Anchor.TopLeft, Vector2.Zero) { Parent = panel, Size = Box };
            Frame.Add(Clip);
            Bare = new ScreenEntity(Anchor.TopLeft, Vector2.Zero) { Parent = panel, Size = Box };
            Row = new ScreenEntity(Anchor.TopLeft, new Vector2(0f, 8f)) { Parent = Frame, Size = Box };
            Item = new Focusable();
            Row.Add(Item);
            Menu = new Menu(FocusFixtures.Item(Vector2.Zero), Item);
        }
    }
}
