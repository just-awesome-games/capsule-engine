using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.Tests.Performance;

[Collection(StagePerformanceCollection.Name)]
public sealed class MenuPerformanceTests
{
    private static readonly InputAction Up = new("Up");
    private static readonly InputAction Down = new("Down");
    private static readonly InputAction Left = new("Left");
    private static readonly InputAction Right = new("Right");
    private static readonly InputAction Confirm = new("Confirm");
    private static readonly InputAction Click = new("Click");

    // A menu is stepped every step it is open, pointer hit test and all, so it is on the step budget
    // like anything else the simulation runs.
    [Fact]
    public void AFocusStepOverAPointerAndEveryAction_AllocatesNothing()
    {
        Scene scene = new();
        Focusable[] items = new Focusable[8];
        FocusNavigator focus = new(new FocusActions(Up, Down, Left, Right, Confirm, Click));

        // Subscribed, so the measured steps raise through live handlers rather than past null ones.
        int focused = 0;
        int unfocused = 0;
        int pressed = 0;
        int moves = 0;

        for (int i = 0; i < items.Length; i++)
        {
            Focusable item = new(new Vector2(20f, 10f));
            items[i] = item;

            item.Focused += () => focused++;
            item.Unfocused += () => unfocused++;
            item.Pressed += () => pressed++;

            focus.Add(item);
            scene.Add(new Holder(new Vector2(0f, i * 20f), item));
        }

        focus.FocusChanged += _ => moves++;
        scene.Add(new Holder(Vector2.Zero, focus));

        InputState input = new(new ActionBindings()
            .Bind(Up, Key.Up)
            .Bind(Down, Key.Down)
            .Bind(Left, Key.Left)
            .Bind(Right, Key.Right)
            .Bind(Confirm, Key.Enter)
            .Bind(Click, MouseButton.Left));

        // Starting the run starts the navigator, which is where its first item takes the focus. The
        // steps are then driven straight into the component, so what is measured is its own work.
        using SceneRun run = new(scene, input, canvas: new Vector2(320f, 180f));

        // The expensive step: the pointer moved, it is inside the last item, and the click is pressed,
        // so both hit tests walk the whole list.
        DeviceSnapshot clicked = DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(new Vector2(10f, 145f));
        DeviceSnapshot released = DeviceSnapshot.Empty.WithPointer(new Vector2(10f, 146f));

        for (int i = 0; i < 100; i++)
        {
            Step(focus, input, i % 2 == 0 ? clicked : released, run.StepSeconds, i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            Step(focus, input, i % 2 == 0 ? clicked : released, run.StepSeconds, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Same(items[^1], focus.Focused);

        // The start and the pointer's one landing, and the click on every other step.
        Assert.Equal(2, moves);
        Assert.Equal(2, focused);
        Assert.Equal(1, unfocused);
        Assert.Equal(550, pressed);
    }

    private static void Step(
        FocusNavigator focus,
        InputState input,
        in DeviceSnapshot snapshot,
        double stepSeconds,
        long tick)
    {
        input.Advance(in snapshot);

        StepContext context = new(stepSeconds, input, tick);
        focus.OnStep(in context);
    }

    // A screen entity, which is what the pointer hit-tests: a world item is skipped before its bounds are
    // read, so a list of those would measure no hit test at all.
    private sealed class Holder : ScreenEntity
    {
        internal Holder(Vector2 position, Component held)
            : base(Anchor.TopLeft, position) =>
            Add(held);
    }
}
