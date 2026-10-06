using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusInteractableTests
{
    // A rebinding capture owns the input while this is false: the menu behind it holds still against
    // both a direction press and a pointer hover, and resuming does not repeat a move the frozen steps
    // would otherwise have earned.
    [Fact]
    public void InteractableFalse_HoldsFocusAgainstADirectionAndAPointerHover_AndDoesNotRepeatOnResume()
    {
        using Menu menu = Column().Open();
        menu.Navigator.RepeatDelay = 3;
        menu.Navigator.RepeatInterval = 1;

        menu.Navigator.Interactable = false;

        menu.Hold(Key.Down);
        menu.Rest();
        menu.Rest();
        menu.Pointer(InSecond);
        menu.Rest();

        Assert.Equal(0, menu.FocusedIndex);
        Assert.Empty(menu.Log);

        menu.Navigator.Interactable = true;
        menu.Rest();

        Assert.Equal(0, menu.FocusedIndex);
    }

    // The bug: an item's own OnStep turns Interactable back on the same step a direction is pressed,
    // standing in for a rebinding prompt whose capture ends on that press. The navigator must not read
    // that step, or the press that closed the prompt would move the focus behind it.
    [Fact]
    public void InteractableTurningTrueOnAPressedStep_ReadsNothingThatStep_AndTheNextFreshPressMoves()
    {
        ArmedItem armed = new();
        Focusable second = Item(new Vector2(0f, 40f));

        using Menu menu = new(armed.Focusable, second);
        armed.Navigator = menu.Navigator;
        menu.Open();

        menu.Navigator.Interactable = false;

        menu.Tap(Key.Down);

        Assert.Equal(0, menu.FocusedIndex);

        menu.Rest();
        menu.Tap(Key.Down);

        Assert.Equal(1, menu.FocusedIndex);
    }

    // Cancel comes after the step's move, and an empty menu can still be backed out of. A capture that
    // owns the input keeps it, as it does every other action.
    [Fact]
    public void Cancel_IsRaisedAfterTheStepsMove_AndNotWhileNotInteractable()
    {
        using Menu menu = Column().Open();

        menu.Tap(Key.Down, Key.Escape);
        Assert.Equal(["unfocused 0", "focused 1", "changed 1", "canceled"], menu.Log);

        menu.Navigator.Interactable = false;
        menu.Tap(Key.Escape);
        Assert.Empty(menu.Log);

        menu.Navigator.Interactable = true;
        menu.Rest().Unseated().Tap(Key.Escape);
        Assert.Equal(["unfocused 1", "canceled"], menu.Log);
    }

    // A press that opens a rebinding prompt owns the input from then on. The cancel pressed with it in
    // the same step does not leave the menu.
    [Fact]
    public void APressHandlerTurningInteractableOff_DropsThatStepsCancel()
    {
        using Menu menu = Column().Open();
        menu.At(0).Pressed += () => menu.Navigator.Interactable = false;

        menu.Tap(Key.Enter, Key.Escape);

        Assert.Equal(["pressed 0"], menu.Log);
    }

    // Sits where a menu item would, so it steps before the navigator, and arms Interactable on the
    // same step Down is pressed.
    private sealed class ArmedItem : ScreenEntity
    {
        internal ArmedItem()
            : base(Anchor.TopLeft, Vector2.Zero) =>
            Add(Focusable = new Focusable(Box));

        internal Focusable Focusable { get; }

        internal FocusNavigator? Navigator { get; set; }

        protected internal override void OnStep(in StepContext context)
        {
            if (context.Input.WasPressed(Down) && Navigator is { } navigator)
            {
                navigator.Interactable = true;
            }
        }
    }
}
