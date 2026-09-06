using System.Globalization;
using System.Text;

namespace Capsule.Input;

// The tape's text grammar, in one place so reading and writing cannot drift apart.
//
//   line   := repeat (' ' token)*
//   repeat := a decimal count of consecutive identical steps, at least 1
//   token  := key | "Pad." pad-button | "Axis." axis '=' value
//
// Tokens are written in enum order — keys, then pad buttons, then axes — so the same tape always
// produces the same bytes; they are read in any order. An axis at rest is written as no token.
internal static class InputTapeText
{
    internal const char Comment = '#';

    private const char Separator = ' ';
    private const char Assign = '=';
    private const string PadPrefix = "Pad.";
    private const string AxisPrefix = "Axis.";

    // None is the empty set on each enum, never a member, so it is written and read by no token.
    private static readonly Key[] Keys = Without(Enum.GetValues<Key>(), Key.None);
    private static readonly PadButton[] PadButtons = Without(Enum.GetValues<PadButton>(), PadButton.None);
    private static readonly PadAxis[] Axes = Without(Enum.GetValues<PadAxis>(), PadAxis.None);

    private static readonly Dictionary<string, Key> KeysByName = ByName(Keys);
    private static readonly Dictionary<string, PadButton> PadButtonsByName = ByName(PadButtons);
    private static readonly Dictionary<string, PadAxis> AxesByName = ByName(Axes);

    internal static void WriteLine(StringBuilder builder, in DeviceSnapshot snapshot, int repeat)
    {
        builder.Append(repeat.ToString(CultureInfo.InvariantCulture));

        foreach (Key key in Keys)
        {
            if (snapshot.IsDown(key))
            {
                builder.Append(Separator).Append(key.ToString());
            }
        }

        foreach (PadButton button in PadButtons)
        {
            if (snapshot.IsDown(button))
            {
                builder.Append(Separator).Append(PadPrefix).Append(button.ToString());
            }
        }

        foreach (PadAxis axis in Axes)
        {
            float value = snapshot.Axis(axis);
            if (value != 0f)
            {
                builder.Append(Separator).Append(AxisPrefix).Append(axis.ToString()).Append(Assign)
                    // The default float format is the shortest text that round-trips.
                    .Append(value.ToString(null, CultureInfo.InvariantCulture));
            }
        }

        builder.Append('\n');
    }

    // Appends the line's run of identical steps to steps.
    internal static void ParseLine(ReadOnlySpan<char> line, int number, List<DeviceSnapshot> steps)
    {
        Enumerator tokens = new(line);

        if (!tokens.MoveNext() ||
            !int.TryParse(tokens.Current, NumberStyles.None, CultureInfo.InvariantCulture, out int repeat) ||
            repeat < 1)
        {
            throw Malformed(number, "a line begins with a positive decimal repeat count.");
        }

        if (repeat > Array.MaxLength - steps.Count)
        {
            throw Malformed(number, $"the tape's repeat counts total more than {Array.MaxLength} steps.");
        }

        DeviceSnapshot snapshot = DeviceSnapshot.Empty;
        while (tokens.MoveNext())
        {
            snapshot = Apply(snapshot, tokens.Current, number);
        }

        for (int i = 0; i < repeat; i++)
        {
            steps.Add(snapshot);
        }
    }

    private static DeviceSnapshot Apply(in DeviceSnapshot snapshot, ReadOnlySpan<char> token, int number)
    {
        if (token.StartsWith(AxisPrefix, StringComparison.Ordinal))
        {
            return ApplyAxis(snapshot, token[AxisPrefix.Length..], number);
        }

        if (token.StartsWith(PadPrefix, StringComparison.Ordinal))
        {
            if (!PadButtonsByName.TryGetValue(token[PadPrefix.Length..].ToString(), out PadButton button))
            {
                throw Malformed(number, $"'{token}' names no pad button.");
            }

            if (snapshot.IsDown(button))
            {
                throw Malformed(number, $"'{token}' is held twice.");
            }

            return snapshot.With(button);
        }

        if (!KeysByName.TryGetValue(token.ToString(), out Key key))
        {
            throw Malformed(number, $"'{token}' names no key, and is prefixed '{PadPrefix}' for no pad button and '{AxisPrefix}' for no axis.");
        }

        if (snapshot.IsDown(key))
        {
            throw Malformed(number, $"'{token}' is held twice.");
        }

        return snapshot.With(key);
    }

    private static DeviceSnapshot ApplyAxis(in DeviceSnapshot snapshot, ReadOnlySpan<char> token, int number)
    {
        int assign = token.IndexOf(Assign);
        if (assign < 0)
        {
            throw Malformed(number, $"'{AxisPrefix}{token}' carries no '{Assign}' and its value.");
        }

        if (!AxesByName.TryGetValue(token[..assign].ToString(), out PadAxis axis))
        {
            throw Malformed(number, $"'{AxisPrefix}{token[..assign]}' names no axis.");
        }

        if (!float.TryParse(token[(assign + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw Malformed(number, $"'{token[(assign + 1)..]}' is no axis value.");
        }

        if (snapshot.Axis(axis) != 0f)
        {
            throw Malformed(number, $"'{AxisPrefix}{token[..assign]}' is placed twice.");
        }

        try
        {
            return snapshot.WithAxis(axis, value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw Malformed(number, exception.Message);
        }
    }

    private static FormatException Malformed(int number, string reason) =>
        new($"Input tape line {number}: {reason}");

    private static TEnum[] Without<TEnum>(TEnum[] values, TEnum excluded)
        where TEnum : struct, Enum =>
        [.. values.Where(value => !EqualityComparer<TEnum>.Default.Equals(value, excluded))];

    private static Dictionary<string, TEnum> ByName<TEnum>(TEnum[] values)
        where TEnum : struct, Enum
    {
        Dictionary<string, TEnum> byName = new(values.Length, StringComparer.Ordinal);
        foreach (TEnum value in values)
        {
            byName.Add(value.ToString()!, value);
        }

        return byName;
    }

    // Splits a line on runs of spaces without allocating a token array.
    private ref struct Enumerator(ReadOnlySpan<char> line)
    {
        private ReadOnlySpan<char> _rest = line;

        public ReadOnlySpan<char> Current { get; private set; }

        public bool MoveNext()
        {
            while (!_rest.IsEmpty && _rest[0] == Separator)
            {
                _rest = _rest[1..];
            }

            if (_rest.IsEmpty)
            {
                return false;
            }

            int end = _rest.IndexOf(Separator);
            Current = end < 0 ? _rest : _rest[..end];
            _rest = end < 0 ? default : _rest[(end + 1)..];

            return true;
        }
    }
}
