using System.Globalization;
using System.Text;

namespace Capsule.Input;

// The tape's text grammar, in one place so reading and writing cannot drift apart.
//
//   line   := repeat (' ' token)*
//   repeat := a decimal count of consecutive identical steps, at least 1
//   token  := key | "Pad." pad-button | "Axis." axis '=' value
//   key    := a Key name, or "Key." and the decimal value of one the enum does not name
//
// Tokens are written in value order — keys, then pad buttons, then axes — so the same tape always
// produces the same bytes; they are read in any order. An axis at rest is written as no token.
internal static class InputTapeText
{
    internal const char Comment = '#';

    private const char Separator = ' ';
    private const char Assign = '=';
    private const string KeyPrefix = "Key.";
    private const string PadPrefix = "Pad.";
    private const string AxisPrefix = "Axis.";

    // One token per value DeviceSnapshot can hold, indexed by that value, so the text is total over
    // what a snapshot accepts: a value the enum does not name is written in its decimal form.
    private static readonly string[] KeyTokens = Tokens<Key>(DeviceSnapshot.Capacity, string.Empty, KeyPrefix);
    private static readonly string[] PadTokens = Tokens<PadButton>(DeviceSnapshot.PadCapacity, PadPrefix, PadPrefix);

    // Index 0 is None on each enum: the empty set, never a member, so it is read by no token.
    private static readonly Dictionary<string, int> KeyValuesByToken = ByToken(KeyTokens);
    private static readonly Dictionary<string, int> PadValuesByToken = ByToken(PadTokens);

    private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> KeyValues =
        KeyValuesByToken.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> PadValues =
        PadValuesByToken.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly PadAxis[] Axes = Without(Enum.GetValues<PadAxis>(), PadAxis.None);
    private static readonly Dictionary<string, PadAxis> AxesByName = ByName(Axes);

    private static readonly Dictionary<string, PadAxis>.AlternateLookup<ReadOnlySpan<char>> AxisValues =
        AxesByName.GetAlternateLookup<ReadOnlySpan<char>>();

    internal static void WriteLine(StringBuilder builder, in DeviceSnapshot snapshot, int repeat)
    {
        builder.Append(repeat.ToString(CultureInfo.InvariantCulture));

        for (int value = 1; value < KeyTokens.Length; value++)
        {
            if (snapshot.IsDown((Key)value))
            {
                builder.Append(Separator).Append(KeyTokens[value]);
            }
        }

        for (int value = 1; value < PadTokens.Length; value++)
        {
            if (snapshot.IsDown((PadButton)value))
            {
                builder.Append(Separator).Append(PadTokens[value]);
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

        // Axes are tracked apart from the snapshot: an axis placed at rest leaves no trace in it,
        // and would otherwise be assignable a second time.
        int axesPlaced = 0;

        while (tokens.MoveNext())
        {
            snapshot = Apply(snapshot, ref axesPlaced, tokens.Current, number);
        }

        for (int i = 0; i < repeat; i++)
        {
            steps.Add(snapshot);
        }
    }

    private static DeviceSnapshot Apply(in DeviceSnapshot snapshot, ref int axesPlaced, ReadOnlySpan<char> token, int number)
    {
        if (token.StartsWith(AxisPrefix, StringComparison.Ordinal))
        {
            return ApplyAxis(snapshot, ref axesPlaced, token[AxisPrefix.Length..], number);
        }

        if (token.StartsWith(PadPrefix, StringComparison.Ordinal))
        {
            if (!PadValues.TryGetValue(token, out int value))
            {
                throw Malformed(number, $"'{token}' names no pad button.");
            }

            PadButton button = (PadButton)value;
            if (snapshot.IsDown(button))
            {
                throw Malformed(number, $"'{token}' is held twice.");
            }

            return snapshot.With(button);
        }

        if (!KeyValues.TryGetValue(token, out int keyValue))
        {
            throw Malformed(number, $"'{token}' names no key, and is prefixed '{PadPrefix}' for no pad button and '{AxisPrefix}' for no axis.");
        }

        Key key = (Key)keyValue;
        if (snapshot.IsDown(key))
        {
            throw Malformed(number, $"'{token}' is held twice.");
        }

        return snapshot.With(key);
    }

    private static DeviceSnapshot ApplyAxis(in DeviceSnapshot snapshot, ref int axesPlaced, ReadOnlySpan<char> token, int number)
    {
        int assign = token.IndexOf(Assign);
        if (assign < 0)
        {
            throw Malformed(number, $"'{AxisPrefix}{token}' carries no '{Assign}' and its value.");
        }

        if (!AxisValues.TryGetValue(token[..assign], out PadAxis axis))
        {
            throw Malformed(number, $"'{AxisPrefix}{token[..assign]}' names no axis.");
        }

        if (!float.TryParse(token[(assign + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw Malformed(number, $"'{token[(assign + 1)..]}' is no axis value.");
        }

        int placed = 1 << (int)axis;
        if ((axesPlaced & placed) != 0)
        {
            throw Malformed(number, $"'{AxisPrefix}{token[..assign]}' is placed twice.");
        }

        axesPlaced |= placed;

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

    private static string[] Tokens<TEnum>(int capacity, string named, string unnamed)
        where TEnum : struct, Enum
    {
        string[] tokens = new string[capacity];

        TEnum[] values = Enum.GetValues<TEnum>();
        string[] names = Enum.GetNames<TEnum>();
        for (int i = 0; i < values.Length; i++)
        {
            int value = Convert.ToInt32(values[i], CultureInfo.InvariantCulture);
            if (value >= 0 && value < capacity)
            {
                tokens[value] = named + names[i];
            }
        }

        for (int value = 0; value < capacity; value++)
        {
            tokens[value] ??= unnamed + value.ToString(CultureInfo.InvariantCulture);
        }

        return tokens;
    }

    private static Dictionary<string, int> ByToken(string[] tokens)
    {
        Dictionary<string, int> byToken = new(tokens.Length - 1, StringComparer.Ordinal);
        for (int value = 1; value < tokens.Length; value++)
        {
            byToken.Add(tokens[value], value);
        }

        return byToken;
    }

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
