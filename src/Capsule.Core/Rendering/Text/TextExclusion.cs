namespace Capsule.Rendering;

// The part of one row a run's line lays out in, in font pixels from the run's origin.
internal readonly record struct TextSpan(int Left, int Width);

// The span each row of a run leaves its line. Row r takes Spans[r] below Count, and the whole box from Count
// on. Line L sits in row L modulo Period where Period is positive, and in row L where it is zero.
internal readonly record struct TextExclusion(TextSpan[] Spans, int Count, int Period)
{
    internal void Span(int line, int boxWidth, out int left, out int width)
    {
        int row = Period > 0 ? line % Period : line;
        if (row < Count)
        {
            (left, width) = Spans[row];

            return;
        }

        left = 0;
        width = boxWidth;
    }

    // Whether no row of a page leaves room. Such a run could never place a glyph.
    internal bool FillsEveryRow()
    {
        if (Period <= 0 || Count < Period)
        {
            return false;
        }

        for (int row = 0; row < Period; row++)
        {
            if (Spans[row].Width > 0)
            {
                return false;
            }
        }

        return true;
    }
}
