namespace Capsule.Build.Atlases;

/// <summary>
/// MaxRects, bottom-left fit, no rotation, searched for the smallest page that holds each page's cells.
/// </summary>
/// <remarks>
/// One input packs the same way on every machine. Cells are sorted by height, then width, then key,
/// and fill full-size pages. Every open page is tried before a new one opens. Each page's cells are
/// then packed again in the same order into strips of up to <see cref="WidthsTried"/> widths. The
/// smallest page wins, then the squarer one, then the earlier attempt. The full-size layout is the
/// first attempt, and no page comes out larger than it.
/// </remarks>
internal static class AtlasPacker
{
    /// <summary>Texels kept clear between one cell and the next on either axis.</summary>
    internal const int Spacing = 2;

    /// <summary>A page's extent is rounded up to this.</summary>
    private const int ExtentGranularity = 4;

    // More widths gain under half a percent of area on 600-cell pages and cost time in proportion.
    private const int WidthsTried = 32;

    /// <param name="items">The cells to place, each a key and its extent in texels.</param>
    /// <param name="maxSize">The largest extent a page may reach on either axis.</param>
    /// <returns>Every placement in sorted order, and each page's extent in page order.</returns>
    /// <exception cref="ArgumentException">A cell is empty or larger than a page on either axis.</exception>
    internal static (Placement[] Placements, (int Width, int Height)[] Pages) Pack(IReadOnlyList<(string Key, int Width, int Height)> items, int maxSize)
    {
        (string Key, int Width, int Height)[] ordered = [.. items];
        Array.Sort(ordered, static (a, b) =>
            (b.Height, b.Width).CompareTo((a.Height, a.Width)) is var bySize and not 0 ? bySize : string.CompareOrdinal(a.Key, b.Key));

        List<Bin> bins = [];
        int[] pageOf = new int[ordered.Length];
        int[] xs = new int[ordered.Length];
        int[] ys = new int[ordered.Length];

        for (int i = 0; i < ordered.Length; i++)
        {
            (string key, int width, int height) = ordered[i];
            if (width <= 0 || height <= 0 || width > maxSize || height > maxSize)
            {
                throw new ArgumentException($"'{key}' is {width}x{height}, which no {maxSize}-texel page holds.", nameof(items));
            }

            int page = 0;
            while (true)
            {
                if (page == bins.Count)
                {
                    // The page is Spacing wider on both axes so every cell carries its trailing
                    // gap, and the gap of a cell on the far edge falls off the page.
                    bins.Add(new Bin(maxSize + Spacing, maxSize + Spacing));
                }

                if (bins[page].TryInsert(width + Spacing, height + Spacing, out xs[i], out ys[i]))
                {
                    pageOf[i] = page;
                    break;
                }

                page++;
            }
        }

        Placement[] placements = new Placement[ordered.Length];
        (int Width, int Height)[] pages = new (int, int)[bins.Count];

        for (int page = 0; page < bins.Count; page++)
        {
            // The page's cells, still in sorted order.
            int[] members = [.. Enumerable.Range(0, ordered.Length).Where(i => pageOf[i] == page)];
            (string Key, int Width, int Height)[] cells = [.. members.Select(i => ordered[i])];
            int[] cellX = [.. members.Select(i => xs[i])];
            int[] cellY = [.. members.Select(i => ys[i])];

            // Rounding never passes maxSize, since a cell ends at or before it and maxSize is a
            // multiple of the granularity.
            (int Width, int Height) extent = (RoundUp(members.Max(i => xs[i] + ordered[i].Width)), RoundUp(members.Max(i => ys[i] + ordered[i].Height)));
            Compact(cells, maxSize, ref extent, cellX, cellY);

            for (int m = 0; m < members.Length; m++)
            {
                placements[members[m]] = new Placement(cells[m].Key, page, cellX[m], cellY[m]);
            }

            pages[page] = extent;
        }

        return (placements, pages);
    }

    private static int RoundUp(int extent) =>
        (extent + ExtentGranularity - 1) / ExtentGranularity * ExtentGranularity;

    // Packs one page's cells into strips of fixed width and open height, and keeps any layout
    // better than the one held in extent, x and y.
    private static void Compact((string Key, int Width, int Height)[] cells, int maxSize, ref (int Width, int Height) extent, int[] x, int[] y)
    {
        int[] tryX = new int[cells.Length];
        int[] tryY = new int[cells.Length];
        Bin bin = new(0, 0);

        // Every cell carries its trailing gap, and so does the strip, whose padded height is the page's.
        int paddedHeight = maxSize + Spacing;
        long paddedArea = cells.Sum(static cell => (long)(cell.Width + Spacing) * (cell.Height + Spacing));
        long row = cells.Sum(static cell => (long)cell.Width + Spacing) - Spacing;

        // The narrowest strip holds the widest cell and has the area of every padded cell at full height.
        int minimumPaddedWidth = (int)((paddedArea + paddedHeight - 1) / paddedHeight);
        int narrowest = RoundUp(Math.Max(cells.Max(static cell => cell.Width), minimumPaddedWidth - Spacing));
        int widest = RoundUp((int)Math.Min(row, maxSize));

        // WidthsTried widths span this many intervals. Rounding the step up tries no more widths than that.
        int widthIntervals = WidthsTried - 1;
        int unroundedStep = (widest - narrowest + widthIntervals - 1) / widthIntervals;
        int step = Math.Max(ExtentGranularity, RoundUp(unroundedStep));

        for (int width = narrowest; width <= widest; width += step)
        {
            if (TryStrip(bin, cells, width, maxSize, extent, tryX, tryY, out int usedWidth, out int usedHeight))
            {
                extent = (usedWidth, usedHeight);
                Array.Copy(tryX, x, x.Length);
                Array.Copy(tryY, y, y.Length);
            }
        }
    }

    // Fills a strip width texels wide and maxSize tall, and succeeds only when its page is smaller
    // than best or as large and squarer. An attempt stops as soon as its page outgrows best.
    private static bool TryStrip(Bin bin, (string Key, int Width, int Height)[] cells, int width, int maxSize, (int Width, int Height) best, int[] x, int[] y, out int usedWidth, out int usedHeight)
    {
        bin.Reset(width + Spacing, maxSize + Spacing);
        usedWidth = 0;
        usedHeight = 0;

        for (int i = 0; i < cells.Length; i++)
        {
            (_, int cellWidth, int cellHeight) = cells[i];
            if (!bin.TryInsert(cellWidth + Spacing, cellHeight + Spacing, out x[i], out y[i]))
            {
                return false;
            }

            usedWidth = Math.Max(usedWidth, RoundUp(x[i] + cellWidth));
            usedHeight = Math.Max(usedHeight, RoundUp(y[i] + cellHeight));
            if ((long)usedWidth * usedHeight > (long)best.Width * best.Height)
            {
                return false;
            }
        }

        long area = (long)usedWidth * usedHeight;
        long bestArea = (long)best.Width * best.Height;

        return area < bestArea || (area == bestArea && Math.Max(usedWidth, usedHeight) < Math.Max(best.Width, best.Height));
    }

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        internal int Right => X + Width;

        internal int Bottom => Y + Height;

        internal bool Contains(in Rect other) =>
            other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

        internal bool Overlaps(in Rect other) =>
            other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;
    }

    private sealed class Bin
    {
        private readonly List<Rect> _free = [];
        private readonly List<Rect> _cut = [];

        internal Bin(int width, int height) => Reset(width, height);

        internal void Reset(int width, int height)
        {
            _free.Clear();
            _free.Add(new Rect(0, 0, width, height));
        }

        // Places at the free rectangle whose placement ends highest, then leftmost.
        internal bool TryInsert(int width, int height, out int x, out int y)
        {
            int best = -1;
            int bestBottom = int.MaxValue;
            int bestX = int.MaxValue;

            for (int i = 0; i < _free.Count; i++)
            {
                Rect candidate = _free[i];
                if (candidate.Width < width || candidate.Height < height)
                {
                    continue;
                }

                int bottom = candidate.Y + height;
                if (bottom < bestBottom || (bottom == bestBottom && candidate.X < bestX))
                {
                    best = i;
                    bestBottom = bottom;
                    bestX = candidate.X;
                }
            }

            if (best < 0)
            {
                x = 0;
                y = 0;

                return false;
            }

            Rect placed = new(_free[best].X, _free[best].Y, width, height);
            x = placed.X;
            y = placed.Y;
            Split(placed);

            return true;
        }

        // Each free rectangle the placement cuts is replaced by the up to four maximal rectangles
        // around it. A new rectangle contained in any other is dropped. An untouched rectangle
        // never lies inside a new one, since each new one lies inside a rectangle that was cut.
        private void Split(in Rect placed)
        {
            _cut.Clear();
            for (int i = _free.Count - 1; i >= 0; i--)
            {
                Rect free = _free[i];
                if (!free.Overlaps(placed))
                {
                    continue;
                }

                _free.RemoveAt(i);

                if (placed.X > free.X)
                {
                    _cut.Add(new Rect(free.X, free.Y, placed.X - free.X, free.Height));
                }

                if (placed.Right < free.Right)
                {
                    _cut.Add(new Rect(placed.Right, free.Y, free.Right - placed.Right, free.Height));
                }

                if (placed.Y > free.Y)
                {
                    _cut.Add(new Rect(free.X, free.Y, free.Width, placed.Y - free.Y));
                }

                if (placed.Bottom < free.Bottom)
                {
                    _cut.Add(new Rect(free.X, placed.Bottom, free.Width, free.Bottom - placed.Bottom));
                }
            }

            int kept = _free.Count;
            for (int i = 0; i < _cut.Count; i++)
            {
                Rect cut = _cut[i];
                bool covered = false;
                for (int j = 0; j < kept && !covered; j++)
                {
                    covered = _free[j].Contains(cut);
                }

                // Of two equal new rectangles only the first survives.
                for (int j = 0; j < _cut.Count && !covered; j++)
                {
                    covered = j != i && _cut[j].Contains(cut) && (j < i || !cut.Contains(_cut[j]));
                }

                if (!covered)
                {
                    _free.Add(cut);
                }
            }
        }
    }
}
