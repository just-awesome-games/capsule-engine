namespace Capsule.Rendering;

// Lends the views a host's simulations draw into. A returned view keeps its capacity for the next borrower, and
// a simulation drawing beside another borrows a view of its own. A view is lent to one simulation at a time.
internal sealed class ViewLender
{
    private readonly Stack<FrameView> _returned = [];

    // The view returned last, or a new one when every view is lent.
    internal FrameView Borrow() => _returned.TryPop(out FrameView? view) ? view : new FrameView();

    internal void Return(FrameView view) => _returned.Push(view);
}
