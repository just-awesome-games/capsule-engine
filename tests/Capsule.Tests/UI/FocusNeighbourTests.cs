using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusNeighbourTests
{
    // The named neighbour is read before the geometry, and only for the direction it is named on: right
    // from the top-left item is the diagonal it names, while down from the same item is still the score.
    [Fact]
    public void ANamedNeighbour_IsMovedToWhereTheGeometryWouldReachAnotherItem()
    {
        using Menu menu = Grid();

        menu.At(0).Right = menu.At(3);

        Assert.Equal(3, menu.Open().Tap(Key.Right).FocusedIndex);

        menu.FocusOn(0);

        Assert.Equal(2, menu.Tap(Key.Down).FocusedIndex);
    }

    // An item named as its own neighbour is how a menu closes an edge off: that direction does nothing
    // at all, rather than wrapping as it would with no neighbour named.
    [Fact]
    public void AnItemNamingItself_BlocksThatDirectionAndLeavesTheOthersAlone()
    {
        using Menu menu = Column();

        menu.At(0).Up = menu.At(0);

        menu.Open().Tap(Key.Up);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);

        Assert.Equal(1, menu.Tap(Key.Down).FocusedIndex);
    }

    // The wrap belongs to the geometry: a named neighbour on the last item of a column is followed, not
    // passed over for the item farthest the other way.
    [Fact]
    public void ANamedNeighbourPastAnEdge_IsFollowedRatherThanWrapping()
    {
        using Menu menu = Triple();

        menu.At(2).Down = menu.At(1);

        Assert.Equal(1, menu.Open().FocusOn(2).Tap(Key.Down).FocusedIndex);
    }

    // What keeps a hand-wired column working while the game has one of its items out of the scene: the
    // move carries on through the missing item's own neighbour.
    [Fact]
    public void ANamedNeighbourThatIsNotLive_HandsTheMoveOnToTheOneItNames()
    {
        using Menu menu = Grid();

        menu.At(0).Down = menu.At(1);
        menu.At(1).Down = menu.At(3);

        Assert.Equal(3, menu.Remove(1).Open().Tap(Key.Down).FocusedIndex);
    }

    [Fact]
    public void AChainOfNamedNeighboursEndingNowhere_LeavesTheFocusWhereItIs()
    {
        using Menu menu = Grid();

        menu.At(0).Down = menu.At(1);

        menu.Remove(1).Open().Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    // Two items naming each other with one of them gone: the walk ends instead of running round.
    [Fact]
    public void AChainOfNamedNeighboursComingBackOnItself_LeavesTheFocusWhereItIs()
    {
        using Menu menu = Column();

        menu.At(0).Down = menu.At(1);
        menu.At(1).Down = menu.At(0);

        menu.Remove(1).Open().Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }

    // One screen's menus are one navigator: an item naming one another navigator holds is a wiring
    // bug, and the add that would hide it is refused.
    [Fact]
    public void AnItemNamingANeighbourTheNavigatorDoesNotHold_IsRefusedWhereItIsAdded()
    {
        using Menu menu = Column();
        Focusable stranger = Item(new Vector2(0f, 80f));
        Focusable outsider = Item(new Vector2(0f, 120f));
        outsider.Down = stranger;

        Assert.Throws<ArgumentException>(() => menu.Navigator.Add(outsider));
    }

    // A neighbour named after the add is never checked, so a direction whose chain reaches an item
    // this navigator does not hold moves nothing.
    [Fact]
    public void ANamedNeighbourTheNavigatorDoesNotHold_MovesNothing()
    {
        using Menu menu = Column().Open();

        menu.At(0).Down = Item(new Vector2(0f, 80f));
        menu.Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);
    }
}
