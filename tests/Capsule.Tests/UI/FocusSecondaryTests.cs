using Capsule.Input;
using Capsule.UI;

using static Capsule.Tests.UI.FocusFixtures;

using Menu = Capsule.Tests.UI.FocusFixtures.Menu;

namespace Capsule.Tests.UI;

public sealed class FocusSecondaryTests
{
    // A right-click after a keyboard move must reach the row under the resting pointer, not the row the
    // keyboard left the focus on.
    [Fact]
    public void ASecondaryAction_PressesTheFocusedItemByKeyAndTheItemUnderThePointerByMouse_AndYieldsToConfirm()
    {
        using Menu menu = Column().Open();

        menu.Pointer(InSecond).Rest().Tap(Key.Up).Tap(Key.Delete);

        Assert.Equal(["Reset 0"], menu.Log);

        menu.Tap(MouseButton.Right);

        Assert.Equal(["unfocused 0", "focused 1", "changed 1", "Reset 1"], menu.Log);

        menu.Tap(Key.Enter, Key.Delete);

        Assert.Equal(["pressed 1"], menu.Log);
    }

    [Fact]
    public void ConfirmListedAsSecondary_IsRefused() =>
        Assert.Throws<ArgumentException>(() => new FocusNavigator(Actions with { Secondary = [Confirm] }));
}
