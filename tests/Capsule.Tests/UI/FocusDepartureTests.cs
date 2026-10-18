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
        (ScreenHolder root, _, Focusable first, _) = Menu(listed: false);
        int unfocused = 0;
        first.Unfocused += () => unfocused++;

        Scene scene = new();
        scene.Add(root);
        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });
        Assert.True(first.IsFocused);

        scene.Remove(root);
        run.Step();

        Assert.False(first.IsFocused);

        FocusNavigator next = new(Actions);
        ScreenHolder nextRoot = new(new Vector2(100f, 0f), next);
        Focusable leading = Under(nextRoot, Vector2.Zero);
        first.Entity!.Parent = nextRoot;
        scene.Add(nextRoot);
        run.Step();

        Assert.Same(leading, next.Focused);
        Assert.False(first.IsFocused);
        Assert.Equal(0, unfocused);
    }

    // The same menu added back after a teardown gives its remembered focus back to the item it left with,
    // raising nothing, whether it gathers its items or lists them. Its next steps keep the focus there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMenuAddedBackAfterATeardown_RestoresItsFocusedItemQuietly(bool listed)
    {
        (ScreenHolder root, FocusNavigator navigator, Focusable first, Focusable second) = Menu(listed);

        Scene scene = new();
        scene.Add(root);
        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });
        navigator.Focus(second);
        int raised = 0;
        first.Focused += () => raised++;
        second.Focused += () => raised++;
        second.Unfocused += () => raised++;
        navigator.FocusChanged += _ => raised++;

        scene.Remove(root);
        run.Step();
        scene.Add(root);
        run.Step();
        run.Step();

        Assert.Same(second, navigator.Focused);
        Assert.True(second.IsFocused);
        Assert.False(first.IsFocused);
        Assert.Equal(0, raised);
    }

    // A column of two items, gathered by its navigator or listed to it.
    private static (ScreenHolder Root, FocusNavigator Navigator, Focusable First, Focusable Second) Menu(bool listed)
    {
        Focusable first = new(Box);
        Focusable second = new(Box);
        FocusNavigator navigator = listed ? new(Actions, first, second) : new(Actions);
        ScreenHolder root = new(Vector2.Zero, navigator);
        _ = new ScreenHolder(Vector2.Zero, first) { Parent = root };
        _ = new ScreenHolder(new Vector2(0f, 40f), second) { Parent = root };

        return (root, navigator, first, second);
    }

    private static Focusable Under(Entity parent, Vector2 position)
    {
        Focusable item = new(Box);
        _ = new ScreenHolder(position, item) { Parent = parent };

        return item;
    }
}
