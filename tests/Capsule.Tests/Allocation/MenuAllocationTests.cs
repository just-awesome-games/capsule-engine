using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.Tests.UI;
using Capsule.UI;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class MenuAllocationTests
{
    // A menu is stepped every step it is open, pointer hit test and all. It is on the step budget like
    // anything else the simulation runs.
    [Fact]
    public void AFocusStepOverAPointerAndEveryAction_AllocatesNothing()
    {
        const int Warmup = 99;
        const int Measured = 999;

        Scene scene = new();
        Focusable[] items = new Focusable[8];
        FocusNavigator focus = new(FocusFixtures.Actions);

        // Subscribed, which makes the measured steps raise through live handlers rather than past null ones.
        int focused = 0;
        int unfocused = 0;
        int pressed = 0;
        int adjusted = 0;
        int dragged = 0;
        int moves = 0;

        for (int i = 0; i < items.Length; i++)
        {
            Focusable item = new(new Vector2(20f, 10f));
            items[i] = item;

            item.Focused += () => focused++;
            item.Unfocused += () => unfocused++;
            item.Pressed += () => pressed++;
            item.Adjusted += _ => adjusted++;
            item.Dragged += _ => dragged++;

            focus.Add(item);

            // On a screen entity, which the pointer hit-tests. A world item is skipped before its
            // bounds are read.
            scene.Add(new FocusFixtures.ScreenHolder(new Vector2(0f, i * 20f), item));
        }

        items[^1].Adjusts = Axis.Horizontal;
        focus.FocusChanged += _ => moves++;
        scene.Add(new FocusFixtures.ScreenHolder(Vector2.Zero, focus));

        InputState input = new(FocusFixtures.Bound(new ActionBindings()));

        // Starting the run starts the navigator, which is where its first item takes the focus. The
        // steps are then driven straight into the component, and what is measured is its own work.
        using SimulationHost run = new(scene, run: new Run { Canvas = new Vector2(320f, 180f) });

        // The expensive step has the pointer moved inside the last item with the click pressed. Both hit
        // tests walk the whole list. The held click then drags the item and the wheel turns over it
        // before the release.
        DeviceSnapshot clicked = DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(new Vector2(10f, 145f));
        DeviceSnapshot dragging = DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(new Vector2(10f, 146f)).WithScroll(Vector2.UnitY);
        DeviceSnapshot released = DeviceSnapshot.Empty.WithPointer(new Vector2(10f, 147f));
        DeviceSnapshot[] cycle = [clicked, dragging, released];

        long bytes = StepMeasurement.Bytes(i => Step(focus, input, cycle[i % cycle.Length], run.StepSeconds, i), Warmup, Measured);

        Assert.Equal(0, bytes);
        Assert.Same(items[^1], focus.Focused);

        // The start and the pointer's one landing. Each cycle presses once, drags on the press and the
        // move, and turns one notch.
        int cycles = (Warmup + Measured) / cycle.Length;
        Assert.Equal(2, moves);
        Assert.Equal(2, focused);
        Assert.Equal(1, unfocused);
        Assert.Equal(cycles, pressed);
        Assert.Equal(2 * cycles, dragged);
        Assert.Equal(cycles, adjusted);
    }

    // The directions are the other half of a menu's step, and the named neighbours put a walk on that
    // path. A column wired by hand has two of its items out of the scene. One measured press is
    // geometry, one is a named neighbour and one is a chain stepping past both dead items.
    [Fact]
    public void AFocusStepOverEveryShapeOfDirection_AllocatesNothing()
    {
        const int Warmup = 96;
        const int Measured = 1000;

        Scene scene = new();
        Focusable[] items = new Focusable[6];
        FocusNavigator focus = new(FocusFixtures.Actions);

        int focused = 0;
        int unfocused = 0;
        int moves = 0;

        for (int i = 0; i < items.Length; i++)
        {
            Focusable item = new(new Vector2(20f, 10f));
            items[i] = item;

            item.Focused += () => focused++;
            item.Unfocused += () => unfocused++;

            focus.Add(item);

            // Clear of the origin, where the pointer rests. A hit test must never take a step the
            // directions are being measured on.
            scene.Add(new FocusFixtures.ScreenHolder(new Vector2(40f, i * 20f), item));
        }

        focus.FocusChanged += _ => moves++;
        scene.Add(new FocusFixtures.ScreenHolder(Vector2.Zero, focus));

        // Down from the first item is geometry. From the second it is the named jump to items[4]. From
        // items[4] it is the chain through items[2] and items[3], both out of the scene, onto items[5].
        // Up from there is named back to the first, which makes the four presses a cycle.
        items[1].Down = items[4];
        items[4].Down = items[2];
        items[2].Down = items[3];
        items[3].Down = items[5];
        items[5].Up = items[0];

        scene.Remove(items[2].Entity!);
        scene.Remove(items[3].Entity!);

        InputState input = new(FocusFixtures.Bound(new ActionBindings()));

        using SimulationHost run = new(scene, run: new Run { Canvas = new Vector2(320f, 180f) });

        DeviceSnapshot down = DeviceSnapshot.Empty.With(Key.Down);
        DeviceSnapshot up = DeviceSnapshot.Empty.With(Key.Up);

        // Every direction is read on its press edge. Each press is a step holding the key and a step
        // without it, and the cycle of four presses is eight steps long.
        DeviceSnapshot[] cycle = [down, DeviceSnapshot.Empty, down, DeviceSnapshot.Empty, down, DeviceSnapshot.Empty, up, DeviceSnapshot.Empty];

        long bytes = StepMeasurement.Bytes(i => Step(focus, input, cycle[i % cycle.Length], run.StepSeconds, i), Warmup, Measured);

        Assert.Equal(0, bytes);

        // Whole cycles, which leave the focus where the first one started.
        Assert.Same(items[0], focus.Focused);

        // Four moves a cycle, plus the focus the first item took at the start, which is the one move
        // nothing lost the focus for.
        int cycles = (Warmup + Measured) / cycle.Length;
        Assert.Equal((4 * cycles) + 1, moves);
        Assert.Equal((4 * cycles) + 1, focused);
        Assert.Equal(4 * cycles, unfocused);
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
}
