namespace Capsule.Rendering;

// One rect a run's lines flow around, in font pixels from the run's origin. A line's row is LineHeight tall
// and starts Pitch below the row before it. Rows repeat every Rows lines where Rows is positive, and count
// on down the whole run where it is zero. A list of rects would answer Span the same way, over each rect.
internal readonly record struct TextExclusion(Rect Area, int Pitch, int LineHeight, int Rows)
{
    // The part of a boxWidth-wide row that line lays out in: the whole row where the row misses Area, and
    // otherwise the wider side Area leaves, the left on a tie. A row Area spans edge to edge has no width.
    internal void Span(int line, int boxWidth, out int left, out int width)
    {
        int row = Rows > 0 ? line % Rows : line;
        float top = (float)row * Pitch;

        left = 0;
        width = boxWidth;

        if (!(top < Area.Bottom && top + LineHeight > Area.Top && Area.Left < boxWidth && Area.Right > 0f))
        {
            return;
        }

        // The wider side is chosen on the rect's own edges, and only the chosen side is rounded inward.
        float before = Math.Clamp(Area.Left, 0f, boxWidth);
        float after = Math.Clamp(Area.Right, 0f, boxWidth);

        if (before >= boxWidth - after)
        {
            width = (int)MathF.Floor(before);
        }
        else
        {
            left = (int)MathF.Ceiling(after);
            width = boxWidth - left;
        }
    }

    // Whether Area leaves no room in any row of a page. Such a run could never place a glyph.
    internal bool FillsEveryRow(int boxWidth)
    {
        if (Rows <= 0)
        {
            return false;
        }

        for (int row = 0; row < Rows; row++)
        {
            Span(row, boxWidth, out _, out int width);
            if (width > 0)
            {
                return false;
            }
        }

        return true;
    }
}
