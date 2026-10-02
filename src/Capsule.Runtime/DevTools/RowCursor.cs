namespace Capsule.Runtime.DevTools;

// Not FocusNavigator: the overlay's scene never steps, and its rows are a window over a longer page.
internal sealed class RowCursor(IReadOnlyList<OverlayRow> rows)
{
    // Wheel notches not yet applied. A touchpad reports fractions.
    private float _scrollRemainder;

    internal int Focus { get; private set; }

    // The wheel moves it away from the focus.
    internal int Top { get; private set; }

    private int MaxTop => Math.Max(0, rows.Count - OverlayScene.MaxRows);

    internal void Reset(int focus)
    {
        Focus = focus;
        Top = 0;
        _scrollRemainder = 0f;
    }

    // Keeps the focus on an interactive row and the window on the page.
    internal void Fit()
    {
        Focus = NearestInteractive(Math.Clamp(Focus, 0, Math.Max(0, rows.Count - 1)));
        Top = Math.Clamp(Top, 0, MaxTop);
    }

    // Wraps at either end, then scrolls the window the least that shows the focus.
    internal void Move(int step)
    {
        for (int moved = 1; moved <= rows.Count; moved++)
        {
            int index = ((Focus + (step * moved)) % rows.Count + rows.Count) % rows.Count;
            if (rows[index].Activate is not null)
            {
                Focus = index;
                Top = Math.Clamp(Top, Focus - OverlayScene.MaxRows + 1, Focus);

                return;
            }
        }
    }

    // Three rows per notch, up for positive. The fraction carries to the next frame.
    internal void Scroll(float notches)
    {
        if (notches == 0f)
        {
            return;
        }

        _scrollRemainder -= notches * 3f;
        int moved = (int)MathF.Truncate(_scrollRemainder);
        _scrollRemainder -= moved;
        Top = Math.Clamp(Top + moved, 0, MaxTop);
    }

    internal void StopScroll() => _scrollRemainder = 0f;

    // Only an interactive row takes the focus. Returns whether the click chose it.
    internal bool Point(int row, bool moved, bool clicked)
    {
        if (row < 0 || row >= rows.Count || rows[row].Activate is null)
        {
            return false;
        }

        if (moved || clicked)
        {
            Focus = row;
        }

        return clicked;
    }

    // Prefers the earlier row at a tie. Returns index where the page has none.
    private int NearestInteractive(int index)
    {
        for (int distance = 0; distance < rows.Count; distance++)
        {
            if (index - distance >= 0 && rows[index - distance].Activate is not null)
            {
                return index - distance;
            }

            if (index + distance < rows.Count && rows[index + distance].Activate is not null)
            {
                return index + distance;
            }
        }

        return index;
    }
}
