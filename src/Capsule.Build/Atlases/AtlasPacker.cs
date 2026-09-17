namespace Capsule.Build.Atlases;

/// <summary>Where one cell landed: its page and the top-left texel of its cell on that page.</summary>
internal readonly record struct Placement(string Key, int Page, int X, int Y);

/// <summary>
/// MaxRects, best short side fit, no rotation. The input is sorted by height, then width, then key,
/// and every tie breaks on the first free rectangle found, so one input packs the same way on every
/// machine. A cell that fits no open page opens the next; every open page is tried first, so a
/// small cell late in the order fills a hole an earlier page left.
/// </summary>
internal static class AtlasPacker
{
    /// <summary>Texels kept clear between one cell and the next on either axis.</summary>
    internal const int Spacing = 2;

    /// <summary>A page's extent is rounded up to this.</summary>
    private const int ExtentGranularity = 4;

    /// <param name="items">The cells to place, each a key and its extent in texels.</param>
    /// <param name="maxSize">The largest extent a page may reach on either axis.</param>
    /// <returns>Every placement in sorted order, and each page's extent in page order.</returns>
    /// <exception cref="ArgumentException">A cell is empty or larger than a page on either axis.</exception>
    internal static (Placement[] Placements, (int Width, int Height)[] Pages) Pack(IReadOnlyList<(string Key, int Width, int Height)> items, int maxSize)
    {
        (string Key, int Width, int Height)[] ordered = [.. items];
        Array.Sort(ordered, static (a, b) =>
        {
            int byHeight = b.Height.CompareTo(a.Height);
            if (byHeight != 0)
            {
                return byHeight;
            }

            int byWidth = b.Width.CompareTo(a.Width);

            return byWidth != 0 ? byWidth : string.CompareOrdinal(a.Key, b.Key);
        });

        List<Bin> pages = [];
        Placement[] placements = new Placement[ordered.Length];

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
                if (page == pages.Count)
                {
                    // Spacing wider than the page on both axes: a cell then always carries its own
                    // trailing gap, and the gap of the cell on the far edge falls off the page.
                    pages.Add(new Bin(maxSize + Spacing, maxSize + Spacing));
                }

                if (pages[page].TryInsert(width + Spacing, height + Spacing, out int x, out int y))
                {
                    placements[i] = new Placement(key, page, x, y);
                    pages[page].Cover(x + width, y + height);
                    break;
                }

                page++;
            }
        }

        // Never past maxSize: a cell ends at or before it and maxSize is itself a multiple.
        return (placements, [.. pages.Select(static page => (RoundUp(page.UsedWidth), RoundUp(page.UsedHeight)))]);
    }

    private static int RoundUp(int extent) =>
        (extent + ExtentGranularity - 1) / ExtentGranularity * ExtentGranularity;

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        internal int Right => X + Width;

        internal int Bottom => Y + Height;

        internal bool Contains(in Rect other) =>
            other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

        internal bool Overlaps(in Rect other) =>
            other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;
    }

    private sealed class Bin(int width, int height)
    {
        private readonly List<Rect> _free = [new Rect(0, 0, width, height)];

        internal int UsedWidth { get; private set; }

        internal int UsedHeight { get; private set; }

        internal void Cover(int right, int bottom)
        {
            UsedWidth = Math.Max(UsedWidth, right);
            UsedHeight = Math.Max(UsedHeight, bottom);
        }

        internal bool TryInsert(int width, int height, out int x, out int y)
        {
            int best = -1;
            int bestShort = int.MaxValue;
            int bestLong = int.MaxValue;

            for (int i = 0; i < _free.Count; i++)
            {
                Rect candidate = _free[i];
                if (candidate.Width < width || candidate.Height < height)
                {
                    continue;
                }

                int shortSide = Math.Min(candidate.Width - width, candidate.Height - height);
                int longSide = Math.Max(candidate.Width - width, candidate.Height - height);
                if (shortSide < bestShort || (shortSide == bestShort && longSide < bestLong))
                {
                    best = i;
                    bestShort = shortSide;
                    bestLong = longSide;
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

        // Every free rectangle the placement cuts is replaced by the up-to-four maximal rectangles
        // left around it; then any free rectangle inside another is dropped.
        private void Split(in Rect placed)
        {
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
                    _free.Add(new Rect(free.X, free.Y, placed.X - free.X, free.Height));
                }

                if (placed.Right < free.Right)
                {
                    _free.Add(new Rect(placed.Right, free.Y, free.Right - placed.Right, free.Height));
                }

                if (placed.Y > free.Y)
                {
                    _free.Add(new Rect(free.X, free.Y, free.Width, placed.Y - free.Y));
                }

                if (placed.Bottom < free.Bottom)
                {
                    _free.Add(new Rect(free.X, placed.Bottom, free.Width, free.Bottom - placed.Bottom));
                }
            }

            for (int i = 0; i < _free.Count; i++)
            {
                for (int j = i + 1; j < _free.Count; j++)
                {
                    if (_free[j].Contains(_free[i]))
                    {
                        _free.RemoveAt(i);
                        i--;
                        break;
                    }

                    if (_free[i].Contains(_free[j]))
                    {
                        _free.RemoveAt(j);
                        j--;
                    }
                }
            }
        }
    }
}
