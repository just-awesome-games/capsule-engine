namespace Capsule.Rendering;

// Lends the views a host's simulations draw into. A returned view keeps its capacity for the next borrower, and
// a simulation drawing beside another borrows a view of its own. A view is lent to one simulation at a time.
internal sealed class ViewLender
{
    private readonly Stack<FrameView> _returned = [];

    // Whether every view lent from here on is drawn by a host that keeps what it drew on the device, which such a host sets.
    internal bool RetainsDrawState { get; set; }

    // The view returned last, or a new one when every view is lent.
    internal FrameView Borrow()
    {
        FrameView view = _returned.TryPop(out FrameView? returned) ? returned : new FrameView();
        view.RetainsDrawState = RetainsDrawState;

        return view;
    }

    // Takes a view back cleared, which holds nothing of the scene that drew it.
    internal void Return(FrameView view)
    {
        view.Clear();
        _returned.Push(view);
    }
}
