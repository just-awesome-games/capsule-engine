using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Performance;

[Collection(StagePerformanceCollection.Name)]
public sealed class MenuPerformanceTests
{
    private static readonly InputAction Backward = new("Backward");
    private static readonly InputAction Forward = new("Forward");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    // A menu is stepped every step it is open, pointer hit test and all, so it is on the step budget
    // like anything else the simulation runs.
    [Fact]
    public void AFocusStepOverAPointerAndEveryAction_AllocatesNothing()
    {
        FocusNavigator<ColorRect> focus = new(new FocusActions(Backward, Forward, Confirm, Click));
        for (int i = 0; i < 8; i++)
        {
            focus.Add(Item(new Vector2(0f, i * 20f)));
        }

        // Subscribed, so the measured steps raise through live handlers rather than past null ones.
        int moves = 0;
        int activations = 0;
        focus.FocusChanged += _ => moves++;
        focus.Activated += _ => activations++;

        ActionBindings bindings = new ActionBindings()
            .Bind(Backward, Key.Up)
            .Bind(Forward, Key.Down)
            .Bind(Confirm, Key.Enter)
            .Bind(Click, MouseButton.Left);

        InputState input = new(bindings);

        // The expensive step: the pointer moved, it is inside the last item, and the click is pressed,
        // so both hit tests walk the whole list.
        DeviceSnapshot clicked = DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(new Vector2(10f, 145f));
        DeviceSnapshot released = DeviceSnapshot.Empty.WithPointer(new Vector2(10f, 146f));

        for (int i = 0; i < 100; i++)
        {
            input.Advance(i % 2 == 0 ? clicked : released);
            focus.Step(input);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            input.Advance(i % 2 == 0 ? clicked : released);
            focus.Step(input);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(7, focus.FocusedIndex);
        Assert.Equal(1, moves);
        Assert.Equal(550, activations);
    }

    private static ColorRect Item(Vector2 position)
    {
        ColorRect rect = new(new Vector2(20f, 10f));
        _ = new Holder(position, rect);

        return rect;
    }

    // A screen entity, which is what the pointer hit-tests: a world item is skipped before its bounds are
    // read, so a list of those would measure no hit test at all.
    private sealed class Holder : ScreenEntity
    {
        internal Holder(Vector2 position, Component drawn)
            : base(Anchor.TopLeft, position)
        {
            Add(drawn);
        }
    }
}
