using System.Globalization;
using Capsule.Diagnostics;

namespace Capsule.Runtime.DevTools.Recordings;

// The ticks --trace or --capture writes, and the ceiling on how many it writes unless --force lifts it. A
// selection a run can count before its first step is refused there, and the run halts at the first tick
// past the ceiling of one that cannot, as an open range a driver plays.
internal sealed class TickSelection(string flag, TickRange[] ranges, int ceiling, bool force)
{
    private const string Fix = "Name the ticks around the moment in question, or give --force to write them all.";

    private long _written;

    // The first tick the ceiling refused, or null while the selection is within it.
    internal long? HaltedAt { get; private set; }

    internal bool Holds(long tick)
    {
        foreach (TickRange range in ranges)
        {
            if (range.Holds(tick))
            {
                return true;
            }
        }

        return false;
    }

    // Whether tick is one to write, counting it as written. False for a tick outside the selection, and for
    // every tick once one has passed the ceiling, the first of which sets HaltedAt.
    internal bool Take(long tick)
    {
        if (HaltedAt is not null || !Holds(tick))
        {
            return false;
        }

        if (!force && _written == ceiling)
        {
            HaltedAt = tick;

            return false;
        }

        _written++;

        return true;
    }

    // Why the selection is refused before the run, or null. lastTick is the run's last, which a recording
    // knows and a driver does not. With none an open range counts no ticks, and the run halts on it instead.
    internal string? Refusal(long? lastTick)
    {
        long count = Count(ranges, lastTick);

        return force || count <= ceiling
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{flag} selects {count} ticks, past its ceiling of {ceiling}. {Fix}");
    }

    // Warns of a tick past finalTick, the run's last, unless the run halted before it.
    internal void WarnPastEnd(long finalTick)
    {
        long last = ranges.Max(static range => range.IsOpen ? range.First : range.Last);
        if (HaltedAt is null && last > finalTick)
        {
            Log.Warning(string.Create(CultureInfo.InvariantCulture, $"{flag} names tick {last}, and the run ended at tick {finalTick}."));
        }
    }

    // Why the run halted, once it has.
    internal string HaltMessage(string written) =>
        string.Create(CultureInfo.InvariantCulture, $"{flag} stopped the run at tick {HaltedAt}, past its ceiling of {ceiling} ticks, and kept the {ceiling} it wrote to {written}. {Fix}");

    // The ticks in ranges from 0 to lastTick, counting a tick two ranges share once. With no lastTick an
    // open range counts none.
    internal static long Count(TickRange[] ranges, long? lastTick)
    {
        long count = 0;
        long next = 0;
        foreach (TickRange range in ranges.OrderBy(static range => range.First))
        {
            long last = range.IsOpen ? lastTick ?? range.First - 1 : Math.Min(range.Last, lastTick ?? long.MaxValue);
            long first = Math.Max(range.First, next);
            if (last >= first)
            {
                count += last - first + 1;
                next = last + 1;
            }
        }

        return count;
    }
}

// The ticks from First to Last, both included. An open range runs to the run's end.
internal readonly record struct TickRange(long First, long Last)
{
    internal bool IsOpen => Last == long.MaxValue;

    internal bool Holds(long tick) => tick >= First && tick <= Last;

    // A tick (120), a range (100..160), an open range (100..), or a comma list of them, in whole ticks with
    // each range's first at or before its last. Null when text is none of those.
    internal static TickRange[]? TryParseList(string text)
    {
        string[] parts = text.Split(',');
        TickRange[] ranges = new TickRange[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            int dots = part.IndexOf("..", StringComparison.Ordinal);
            string first = dots < 0 ? part : part[..dots];
            string last = dots < 0 ? part : part[(dots + 2)..];
            long to = long.MaxValue;
            if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out long from)
                || (last.Length > 0 && !long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out to))
                || from > to)
            {
                return null;
            }

            ranges[index] = new TickRange(from, to);
        }

        return ranges;
    }
}
