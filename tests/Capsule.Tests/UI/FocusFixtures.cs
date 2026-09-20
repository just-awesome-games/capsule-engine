using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Tests.UI;

// The shared rig every focus spec is written against: the actions, the boxes, the pointer positions
// inside and between them, and a scene holding one item per entity under one navigator.
internal static class FocusFixtures
{
    internal static readonly InputAction Up = new("Up");

    internal static readonly InputAction Down = new("Down");

    internal static readonly InputAction Left = new("Left");

    internal static readonly InputAction Right = new("Right");

    internal static readonly InputAction Confirm = new("Confirm");

    internal static readonly InputAction Click = new("Click");

    internal static readonly FocusActions Actions = new(Up, Down, Left, Right, Confirm, Click);

    internal static readonly Vector2 Box = new(20f, 10f);

    internal static readonly Vector2 Canvas = new(200f, 120f);

    // A column of two items spanning (0, 0) to (20, 10) and (0, 40) to (20, 50): inside the first,
    // inside the second, and in the gap between them.
    internal static readonly Vector2 InFirst = new(10f, 5f);

    internal static readonly Vector2 InSecond = new(10f, 45f);

    internal static readonly Vector2 InNeither = new(10f, 25f);

    internal static Menu Column() => new(Item(Vector2.Zero), Item(new Vector2(0f, 40f)));

    // The column with a third box on the same 40-pixel centres, for the cases that need an item past
    // the one they remove or redirect away from.
    internal static Menu Triple() => new(Item(Vector2.Zero), Item(new Vector2(0f, 40f)), Item(new Vector2(0f, 80f)));

    // A 2x2 grid of 20x10 boxes on 40-pixel centres, in reading order: top-left, top-right,
    // bottom-left, bottom-right.
    internal static Menu Grid() =>
        new(
            Item(Vector2.Zero),
            Item(new Vector2(40f, 0f)),
            Item(new Vector2(0f, 40f)),
            Item(new Vector2(40f, 40f)));

    internal static Focusable Item(Vector2 position, Vector2? size = null)
    {
        Focusable item = new(size ?? Box);
        _ = new ScreenHolder(position, item);

        return item;
    }

    internal static Focusable WorldItem(Vector2 position)
    {
        Focusable item = new(Box);
        _ = new WorldHolder(position, item);

        return item;
    }

    internal sealed class ScreenHolder : ScreenEntity
    {
        internal ScreenHolder(Vector2 position, Component item)
            : base(Anchor.TopLeft, position) =>
            Add(item);
    }

    internal sealed class WorldHolder : Entity
    {
        internal WorldHolder(Vector2 position, Component item)
            : base(position) =>
            Add(item);
    }

    // A scene holding one item per entity and a navigator over all of them, stepped once per call, so a
    // spec reads as the sequence of steps it means. The log carries what the step just taken raised, in
    // order, and the items are named by their place in the navigator's list.
    internal sealed class Menu : IDisposable
    {
        private readonly Scene _scene = new();
        private readonly List<string> _log = [];
        private readonly List<Focusable> _items = [];

        private SimulationHost? _run;
        private DeviceSnapshot _held;

        internal Menu(params ReadOnlySpan<Focusable> items)
            : this(Actions, items)
        {
        }

        internal Menu(FocusActions actions, params ReadOnlySpan<Focusable> items)
        {
            Navigator = new FocusNavigator(actions);

            for (int i = 0; i < items.Length; i++)
            {
                Watch(items[i]);
                Navigator.Add(items[i]);
            }

            Navigator.FocusChanged += item => _log.Add($"changed {IndexOf(item)}");
            _scene.Add(new WorldHolder(Vector2.Zero, Navigator));
        }

        internal FocusNavigator Navigator { get; }

        /// <summary>What the step just taken raised, in the order it was raised.</summary>
        internal IReadOnlyList<string> Log => _log;

        /// <summary>Which item has the focus, as its place in the list, or -1 where none has it.</summary>
        internal int FocusedIndex => IndexOf(Navigator.Focused);

        /// <summary>
        /// The one item reporting <see cref="Focusable.IsFocused"/>, or -1 where none does; -2 where
        /// more than one does, which is the state a half-applied move would leave.
        /// </summary>
        internal int OnlyFocusedIndex
        {
            get
            {
                int only = -1;

                for (int i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].IsFocused)
                    {
                        continue;
                    }

                    if (only >= 0)
                    {
                        return -2;
                    }

                    only = i;
                }

                return only;
            }
        }

        internal Focusable At(int index) => _items[index];

        /// <summary>
        /// An item in the scene and in the log from here on, but not yet one of the navigator's: what
        /// a handler calling <see cref="FocusNavigator.Add"/> mid-flight is handed.
        /// </summary>
        internal Focusable Latecomer(Vector2 position) => Watch(Item(position));

        /// <summary>Takes the entity holding item <paramref name="index"/> out of the scene.</summary>
        internal Menu Remove(int index)
        {
            _scene.Remove(_items[index].Entity!);

            return this;
        }

        /// <summary>Puts it back, which is all an item needs to be navigable again.</summary>
        internal Menu Restore(int index)
        {
            _scene.Add(_items[index].Entity!);

            return this;
        }

        /// <summary>
        /// Takes every item's entity out of the scene, for the cases that must reach the navigator
        /// while none of its items is live; <see cref="Seat"/> puts them all back.
        /// </summary>
        internal Menu Unseated()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Remove(i);
            }

            return this;
        }

        internal Menu Seat()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                Restore(i);
            }

            return this;
        }

        /// <summary>
        /// Has item <paramref name="index"/>, the first time it loses the focus, ask from inside its own
        /// handler for the focus to go to <paramref name="target"/> instead.
        /// </summary>
        internal Menu RedirectOnUnfocused(int index, int target) => Redirect(index, target, onFocused: false);

        /// <summary>The same redirect, asked for as the item takes the focus.</summary>
        internal Menu RedirectOnFocused(int index, int target) => Redirect(index, target, onFocused: true);

        /// <summary>
        /// Has item <paramref name="index"/>, the first time it takes the focus, take its own entity
        /// out of the scene from inside that handler: the item a step is about to press leaving
        /// mid-move.
        /// </summary>
        internal Menu RemoveOnFocused(int index)
        {
            bool spent = false;

            _items[index].Focused += () =>
            {
                if (spent)
                {
                    return;
                }

                spent = true;
                Remove(index);
            };

            return this;
        }

        /// <summary>Starts the scene, which is where the navigator's starting item takes the focus.</summary>
        internal Menu Open()
        {
            Run run = new() { Canvas = Canvas };
            Bound(run.Input.Bindings);
            _run = new SimulationHost(_scene, run: run);

            return this;
        }

        /// <summary>Puts the pointer here from the next step on; emits no step of its own.</summary>
        internal Menu Pointer(Vector2 position)
        {
            _held = _held.WithPointer(position);

            return this;
        }

        /// <summary>Steps with <paramref name="key"/> down on top of the held state, then releases it.</summary>
        internal Menu Tap(Key key) => Advance(_held.With(key));

        /// <summary>Steps with both keys down at once, which is two directions asking for one move.</summary>
        internal Menu Tap(Key first, Key second) => Advance(_held.With(first).With(second));

        /// <summary>Steps with <paramref name="button"/> down on top of the held state, then releases it.</summary>
        internal Menu Tap(MouseButton button) => Advance(_held.With(button));

        /// <summary>Steps with both down at once, which is two actions asking for one press.</summary>
        internal Menu Tap(MouseButton button, Key key) => Advance(_held.With(button).With(key));

        /// <summary>Steps with <paramref name="key"/> down and leaves it held.</summary>
        internal Menu Hold(Key key)
        {
            _held = _held.With(key);

            return Advance(_held);
        }

        /// <summary>Steps with nothing new: the held state exactly as it stands.</summary>
        internal Menu Rest() => Advance(_held);

        /// <summary>Lets go of <paramref name="key"/> and steps.</summary>
        internal Menu Release(Key key)
        {
            _held = _held.Without(key);

            return Advance(_held);
        }

        /// <summary>Focuses an item outright, as a game opening a menu on one does; steps nothing.</summary>
        internal Menu FocusOn(int index)
        {
            _log.Clear();
            Navigator.Focus(_items[index]);

            return this;
        }

        /// <summary>
        /// Takes item <paramref name="index"/> out of the navigator, leaving it in the scene and in
        /// the log; steps nothing.
        /// </summary>
        internal bool Drop(int index)
        {
            _log.Clear();

            return Navigator.Remove(_items[index]);
        }

        public void Dispose() => _run?.Dispose();

        private static ActionBindings Bound(ActionBindings bindings) =>
            bindings
                .Bind(Up, Key.Up)
                .Bind(Down, Key.Down)
                .Bind(Left, Key.Left)
                .Bind(Right, Key.Right)
                .Bind(Confirm, Key.Enter)
                .Bind(Click, MouseButton.Left);

        private Menu Advance(in DeviceSnapshot snapshot)
        {
            _log.Clear();
            _run!.Step(snapshot);

            return this;
        }

        // Into the log under its place in the list, and into the scene, which is what makes it live.
        private Focusable Watch(Focusable item)
        {
            int index = _items.Count;

            item.Focused += () => _log.Add($"focused {index}");
            item.Unfocused += () => _log.Add($"unfocused {index}");
            item.Pressed += () => _log.Add($"pressed {index}");

            _items.Add(item);
            _scene.Add(item.Entity!);

            return item;
        }

        private Menu Redirect(int index, int target, bool onFocused)
        {
            bool spent = false;

            void Ask()
            {
                if (spent)
                {
                    return;
                }

                spent = true;
                Navigator.Focus(_items[target]);
            }

            if (onFocused)
            {
                _items[index].Focused += Ask;
            }
            else
            {
                _items[index].Unfocused += Ask;
            }

            return this;
        }

        private int IndexOf(Focusable? item)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (ReferenceEquals(_items[i], item))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
