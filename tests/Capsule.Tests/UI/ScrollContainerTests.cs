using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

// A scroll container is a clip whose content moves. The specs read the offset it settles on and where the
// items land, and they step the navigator as a player would.
public sealed class ScrollContainerTests
{
    // The rig's padded inside runs from PaddedTop to PaddedBottom.
    private const float PaddedTop = 3f;

    private const float PaddedBottom = 37f;

    [Fact]
    public void TheOffset_ReadsClampedToTheContent_WhenWrittenPastIt_AndWhenTheContentShrinks()
    {
        Rig rig = new();

        Assert.Equal(new Vector2(0f, 26f), rig.Frame.MaxScrollOffset);

        rig.Rows.Size = new Vector2(30f, 0f);
        Assert.Equal(new Vector2(10f, 26f), rig.Frame.MaxScrollOffset);

        rig.Frame.ScrollOffset = new Vector2(50f, 100f);
        Assert.Equal(new Vector2(10f, 26f), rig.Frame.ScrollOffset);
        Assert.Equal(new Vector2(-10f, -23f), rig.Items[0].Bounds.Position);

        rig.Items[5].Entity!.Visible = false;
        rig.Items[4].Entity!.Visible = false;
        rig.Rows.Size = Vector2.Zero;

        Assert.Equal(new Vector2(0f, 6f), rig.Frame.ScrollOffset);
        Assert.Equal(-3f, rig.Items[0].Bounds.Top);
    }

    [Fact]
    public void ADirection_RevealsAnOffViewRowByTheSmallestScroll_AndAHoverNeverScrolls()
    {
        Rig rig = new();
        using Menu menu = rig.Menu.Open();

        Press(menu, Key.Down);
        Press(menu, Key.Down);
        Assert.Equal(0f, rig.Frame.ScrollOffset.Y);

        Press(menu, Key.Down);
        Assert.Equal(6f, rig.Frame.ScrollOffset.Y);
        Assert.Equal(PaddedBottom, rig.Items[3].Bounds.Bottom);

        Press(menu, Key.Down);
        Assert.Equal(16f, rig.Frame.ScrollOffset.Y);

        Press(menu, Key.Up);
        Press(menu, Key.Up);
        Press(menu, Key.Up);
        Assert.Equal(1, menu.FocusedIndex);
        Assert.Equal(PaddedTop, rig.Items[1].Bounds.Top);
        Assert.Equal(10f, rig.Frame.ScrollOffset.Y);

        // Row 4 reaches past the padded inside into the clip's last three pixels, and a mouse landing
        // there focuses it without moving anything.
        menu.Pointer(new Vector2(10f, 39f)).Rest();
        Assert.Equal(4, menu.FocusedIndex);
        Assert.Equal(10f, rig.Frame.ScrollOffset.Y);
    }

    [Fact]
    public void Focus_RevealsAnOffViewItemInEveryScrollContainerAboveIt()
    {
        Rig rig = new();
        using Menu menu = rig.Menu.Open();

        menu.FocusOn(5);
        Assert.Equal(26f, rig.Frame.ScrollOffset.Y);
        Assert.Equal(PaddedBottom, rig.Items[5].Bounds.Bottom);

        Nested nested = new(outerHeight: 15f);
        using Menu stacked = nested.Menu.Open();

        stacked.FocusOn(5);
        Assert.Equal(new Vector2(0f, 40f), nested.Inner.ScrollOffset);
        Assert.Equal(new Vector2(0f, 5f), nested.Outer.ScrollOffset);
        Assert.Equal(15f, nested.Items[5].Bounds.Bottom);
    }

    [Fact]
    public void TheWheel_ScrollsByTheStepPerNotch_OrAnEighthOfTheInsideWhereTheStepIsZero()
    {
        Rig rig = new();
        using Menu menu = rig.Menu.Open();
        Vector2 over = new(10f, 20f);

        menu.Pointer(over).Wheel(-4f);
        Assert.Equal(17f, rig.Frame.ScrollOffset.Y);

        rig.Frame.WheelStep = 5f;
        menu.Wheel(1f);
        Assert.Equal(12f, rig.Frame.ScrollOffset.Y);

        menu.Wheel(-0.5f);
        Assert.Equal(14.5f, rig.Frame.ScrollOffset.Y);

        menu.Pointer(new Vector2(100f, 20f)).Wheel(-4f);
        Assert.Equal(14.5f, rig.Frame.ScrollOffset.Y);

        rig.Frame.Visible = false;
        menu.Pointer(over).Wheel(-4f);
        Assert.Equal(14.5f, rig.Frame.ScrollOffset.Y);
    }

    [Fact]
    public void TheWheel_LeavesAFocusedAdjustingItemUnderThePointer_OnlyTheAxesItReads()
    {
        Rig rig = new();
        rig.Rows.Size = new Vector2(30f, 0f);
        rig.Items[0].Adjusts = Axis.Vertical;
        using Menu menu = rig.Menu.Open();

        menu.Pointer(new Vector2(10f, 5f)).Wheel(-1f);
        Assert.Equal(["adjusted 0 -1"], menu.Log);
        Assert.Equal(Vector2.Zero, rig.Frame.ScrollOffset);

        menu.Wheel(new Vector2(1f, 0f));
        Assert.Empty(menu.Log);
        Assert.Equal(new Vector2(2.5f, 0f), rig.Frame.ScrollOffset);
    }

    [Fact]
    public void TheWheel_GoesToTheInnermostContainerUnderThePointer_AndNeverChainsAtItsLimit()
    {
        Nested nested = new(outerHeight: 40f);
        using Menu menu = nested.Menu.Open();

        menu.Pointer(new Vector2(10f, 10f)).Wheel(-2f);
        Assert.Equal(new Vector2(0f, 5f), nested.Inner.ScrollOffset);
        Assert.Equal(Vector2.Zero, nested.Outer.ScrollOffset);

        menu.Wheel(-100f);
        Assert.Equal(new Vector2(0f, 40f), nested.Inner.ScrollOffset);
        Assert.Equal(Vector2.Zero, nested.Outer.ScrollOffset);

        menu.Pointer(new Vector2(10f, 30f)).Wheel(-1f);
        Assert.Equal(new Vector2(0f, 5f), nested.Outer.ScrollOffset);
        Assert.Equal(new Vector2(0f, 40f), nested.Inner.ScrollOffset);
    }

    [Fact]
    public void AWheelTurnTheContainerYields_LeavesAnOffsetWrittenPastTheEndInPlace()
    {
        Nested nested = new(outerHeight: 40f);
        using Menu menu = nested.Menu.Open();
        nested.Outer.ScrollOffset = new Vector2(0f, 100f);

        menu.Pointer(new Vector2(10f, 5f)).Wheel(-1f);
        nested.Outer.Size = new Vector2(20f, 20f);

        Assert.Equal(new Vector2(0f, 30f), nested.Outer.ScrollOffset);
    }

    [Fact]
    public void APlacement_OfTheStepBefore_UsesTheOffsetFromBeforeTheStepsWrite()
    {
        Rig rig = new();
        rig.Frame.WheelStep = 5f;
        using Menu menu = rig.Menu.Open();
        Entity row = rig.Items[0].Entity!;

        menu.Pointer(new Vector2(10f, 20f)).Wheel(-2f);
        Assert.Equal(PaddedTop - 10f, row.SpaceOrigin.Y);
        Assert.Equal(PaddedTop, row.PreviousSpaceOrigin.Y);

        menu.Rest();
        Assert.Equal(PaddedTop - 10f, row.PreviousSpaceOrigin.Y);
    }

    // A tap followed by a step with the key up. The next tap is a fresh press edge.
    private static void Press(Menu menu, Key key) => menu.Tap(key).Rest();

    // A 20x40 frame padded 3 pixels top and bottom, over a column of six 10-pixel rows that each hold an item.
    private sealed class Rig
    {
        internal ScrollContainer Frame { get; }

        internal BoxContainer Rows { get; }

        internal List<Focusable> Items { get; } = [];

        internal Menu Menu { get; }

        internal Rig()
        {
            Frame = new ScrollContainer(Anchor.TopLeft, Vector2.Zero) { Size = new Vector2(20f, 40f), Padding = new Insets(0f, PaddedTop, 0f, 3f) };
            Rows = new BoxContainer(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = Frame };

            for (int i = 0; i < 6; i++)
            {
                ScreenEntity row = new(Anchor.TopWide, Vector2.Zero) { Parent = Rows, Size = new Vector2(0f, 10f) };
                Focusable item = new();
                row.Add(item);
                Items.Add(item);
            }

            Menu = new Menu([.. Items]);
        }
    }

    // An outer 20-wide container holding a column of a 20-pixel inner container over six 10-pixel rows, then
    // three more rows of 10. Every row holds an item, in tree order.
    private sealed class Nested
    {
        internal ScrollContainer Outer { get; }

        internal ScrollContainer Inner { get; }

        internal List<Focusable> Items { get; } = [];

        internal Menu Menu { get; }

        internal Nested(float outerHeight)
        {
            Outer = new ScrollContainer(Anchor.TopLeft, Vector2.Zero) { Size = new Vector2(20f, outerHeight) };
            BoxContainer column = new(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = Outer };
            Inner = new ScrollContainer(Anchor.TopWide, Vector2.Zero) { Parent = column, Size = new Vector2(0f, 20f) };
            BoxContainer innerRows = new(Axis.Vertical, Anchor.TopWide, Vector2.Zero) { Parent = Inner };

            Fill(innerRows, 6);
            Fill(column, 3);

            Menu = new Menu([.. Items]);
        }

        private void Fill(ScreenEntity parent, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ScreenEntity row = new(Anchor.TopWide, Vector2.Zero) { Parent = parent, Size = new Vector2(0f, 10f) };
                Focusable item = new();
                row.Add(item);
                Items.Add(item);
            }
        }
    }
}
