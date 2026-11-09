using System.Numerics;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

namespace Capsule.Tests.UI;

public sealed class FocusDepartureTests
{
    // A focused gathered item that leaves its scene stops being focused at once and raises nothing. A
    // torn-down menu never steps again, and the item read later under another navigator must not claim a
    // focus. A listed item stays in its navigator's list, and the navigator's next step releases it.
    [Fact]
    public void AFocusedGatheredItemLeavingItsScene_IsUnfocusedAtOnce_AndRaisesNothing()
    {
        (ScreenHolder root, _, Focusable first, _) = Rooted();
        int unfocused = 0;
        first.Unfocused += () => unfocused++;

        using SimulationHost run = Hosted(root);
        Scene scene = run.Simulation.Scene;
        Assert.True(first.IsFocused);

        scene.Remove(root);
        run.Step();

        Assert.False(first.IsFocused);

        FocusNavigator next = new(Actions);
        ScreenHolder nextRoot = new(new Vector2(100f, 0f), next);
        Focusable leading = Item(Vector2.Zero, parent: nextRoot);
        first.Entity!.Parent = nextRoot;
        scene.Add(nextRoot);
        run.Step();

        Assert.Same(leading, next.Focused);
        Assert.False(first.IsFocused);
        Assert.Equal(0, unfocused);
    }

    // Closing a menu from inside a step or stopping its scene tears down its items with it, raising
    // nothing, whether the navigator gathers its items or lists them. The same menu added back holds each
    // item once and gives its remembered focus back to the item it left with, raising nothing, and its
    // next steps keep the focus there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TearingDownAMenu_RaisesNoFocusEvents_AndAddingItBackRestoresItsFocusQuietly(bool listed)
    {
        (ScreenHolder root, FocusNavigator navigator, Focusable first, Focusable second) = Rooted(listed);
        Closer closer = new();
        using SimulationHost run = Hosted(root, closer);
        navigator.Focus(second);

        List<string> log = [];
        first.Focused += () => log.Add("focused first");
        first.Unfocused += () => log.Add("unfocused first");
        second.Focused += () => log.Add("focused second");
        second.Unfocused += () => log.Add("unfocused second");
        navigator.FocusChanged += _ => log.Add("changed");

        closer.Closing = root;
        run.Step();

        Assert.Empty(log);
        Assert.Same(second, navigator.Focused);
        Assert.Equal(listed ? 2 : 0, navigator.Items.Length);

        run.Simulation.Scene.Add(root);
        run.Step();
        run.Step();

        Assert.Empty(log);
        Assert.Equal([first, second], navigator.Items.ToArray());
        Assert.Same(second, navigator.Focused);
        Assert.True(second.IsFocused);
        Assert.False(first.IsFocused);

        run.Dispose();
        Assert.Empty(log);
    }

    // A focus handler may close another gathered item's entity. The item drops out quietly where an Add,
    // Remove or Focus from the same handler would throw.
    [Fact]
    public void AGatheredItemLeavingFromAFocusHandler_DropsOutQuietly()
    {
        (ScreenHolder root, FocusNavigator navigator, Focusable first, Focusable second) = Rooted();
        using SimulationHost run = Hosted(root);
        navigator.FocusChanged += _ => run.Simulation.Scene.Remove(first.Entity!);

        navigator.Focus(second);

        Assert.Same(second, navigator.Focused);
        Assert.DoesNotContain(first, navigator.Items.ToArray());
    }

    // Removes the entity it is handed from inside its own step, as a menu's close does.
    private sealed class Closer() : ScreenEntity(Anchor.TopLeft, Vector2.Zero)
    {
        internal Entity? Closing { get; set; }

        protected internal override void OnStep(in StepContext context)
        {
            if (Closing is { } closing)
            {
                Closing = null;
                Scene.Remove(closing);
            }
        }
    }
}
