using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Unicode;

namespace Capsule.Localization;

// A key and the arguments that fill it, decoded on demand into a caller's buffer in any language. A keyed
// Label holds one and decodes it again on each language change. Run.GetText decodes one once.
internal sealed class KeyedText
{
    // The text of each argument formatted once at the call, each ending at its entry in _argumentEnds. A
    // key argument is held in _argumentKeys instead, marked in _keyArguments, and resolves with the template.
    private char[] _arguments = [];
    private ArgumentEnds _argumentEnds;
    private ArgumentKeys _argumentKeys;
    private int _keyArguments;
    private ArgumentCounts _counts;
    private int _negatives;
    private int _argumentCount;

    // The key the text resolves from. Default until the first Set.
    internal TextKey Key { get; private set; }

    internal void Set(TextKey text) => Reset(text, 0, 0);

    internal void Set<T0>(TextKey text, T0 arg0)
    {
        ValidateKeyArgument(arg0, nameof(arg0));
        Reset(text, 1, IntegerBit<T0>(0));
        Append(arg0);
    }

    internal void Set<T0, T1>(TextKey text, T0 arg0, T1 arg1)
    {
        ValidateKeyArgument(arg0, nameof(arg0));
        ValidateKeyArgument(arg1, nameof(arg1));
        Reset(text, 2, IntegerBit<T0>(0) | IntegerBit<T1>(1));
        Append(arg0);
        Append(arg1);
    }

    internal void Set<T0, T1, T2>(TextKey text, T0 arg0, T1 arg1, T2 arg2)
    {
        ValidateKeyArgument(arg0, nameof(arg0));
        ValidateKeyArgument(arg1, nameof(arg1));
        ValidateKeyArgument(arg2, nameof(arg2));
        Reset(text, 3, IntegerBit<T0>(0) | IntegerBit<T1>(1) | IntegerBit<T2>(2));
        Append(arg0);
        Append(arg1);
        Append(arg2);
    }

    // Decodes the text in the shipped language closest to tag into buffer from its start, growing it while
    // it is too short, and returns the length written.
    internal int Decode(string? tag, ref char[] buffer)
    {
        ReadOnlySpan<byte> template = Template(Key, tag, out int language);

        return DecodeTemplate(Key.Table, language, template, tag, counted: -1, ref buffer, length: 0);
    }

    // 1 << index when T is an integer type a plural form selects on, else 0. The JIT folds it to a constant.
    private static int IntegerBit<T>(int index) =>
        typeof(T) == typeof(sbyte) || typeof(T) == typeof(byte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort)
        || typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong)
            ? 1 << index
            : 0;

    // An integer value's magnitude, and whether it is negative. The JIT keeps only T's branch.
    private static ulong Magnitude<T>(T value, out bool negative)
    {
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(ushort) || typeof(T) == typeof(uint) || typeof(T) == typeof(ulong))
        {
            negative = false;

            return typeof(T) == typeof(byte) ? Unsafe.As<T, byte>(ref value)
                : typeof(T) == typeof(ushort) ? Unsafe.As<T, ushort>(ref value)
                : typeof(T) == typeof(uint) ? Unsafe.As<T, uint>(ref value)
                : Unsafe.As<T, ulong>(ref value);
        }

        long signed = typeof(T) == typeof(sbyte) ? Unsafe.As<T, sbyte>(ref value)
            : typeof(T) == typeof(short) ? Unsafe.As<T, short>(ref value)
            : typeof(T) == typeof(int) ? Unsafe.As<T, int>(ref value)
            : Unsafe.As<T, long>(ref value);
        negative = signed < 0;

        // Negating long.MinValue wraps to itself, whose bits as a ulong are its magnitude.
        return negative ? unchecked((ulong)-signed) : (ulong)signed;
    }

    // Takes a key, once its argument count is checked and every argument that selects a plural form is an
    // integer, and drops the arguments of any key before it. Nothing changes when it throws.
    private void Reset(TextKey text, int arguments, int integers)
    {
        _ = text.Required(nameof(text));
        if (text.ArgumentCount != arguments)
        {
            throw new ArgumentException(
                $"The text '{text}' takes {text.ArgumentCount} argument(s), and {arguments} were passed. Call the overload taking {text.ArgumentCount}.",
                nameof(text));
        }

        if ((text.PluralMask & ~integers) is not 0 and int refused)
        {
            throw new ArgumentException(
                $"The text '{text}' selects a plural form on argument {int.TrailingZeroCount(refused)}, which was passed no integer. Pass a count of an integer type, sbyte to ulong.",
                nameof(text));
        }

        Key = text;
        _argumentCount = 0;
        _keyArguments = 0;
    }

    // Refuses a key argument no text can show: a null or default key, or one with placeholders of its own.
    // The JIT drops the call for any other value type.
    private static void ValidateKeyArgument<T>(T value, string parameter)
    {
        if (typeof(T) == typeof(TextKey?) && !Unsafe.As<T, TextKey?>(ref value).HasValue)
        {
            throw new ArgumentException("The text key is null and names no text. Pass a key that names one.", parameter);
        }

        if (!TryGetKey(ref value, out TextKey key))
        {
            return;
        }

        _ = key.Required(parameter);
        if (key.ArgumentCount != 0)
        {
            throw new ArgumentException(
                $"The text '{key}' takes {key.ArgumentCount} argument(s) and cannot fill a placeholder. Pass a key taking none, or fill its placeholders in the outer text.",
                parameter);
        }
    }

    // Formats one argument after the ones before it, growing the buffer they share while it is too short.
    // An integer is also kept as the count a plural form selects on, and a key is kept to resolve with the
    // template, taking no room in the buffer.
    private void Append<T>(T value)
    {
        int start = _argumentCount == 0 ? 0 : _argumentEnds[_argumentCount - 1];
        int written;
        if (IntegerBit<T>(0) != 0)
        {
            _counts[_argumentCount] = Magnitude(value, out bool negative);
            _negatives = negative ? _negatives | (1 << _argumentCount) : _negatives & ~(1 << _argumentCount);
        }

        if (TryGetKey(ref value, out TextKey key))
        {
            _argumentKeys[_argumentCount] = key;
            _keyArguments |= 1 << _argumentCount;
            written = 0;
        }
        else if (value is string text)
        {
            Reserve(ref _arguments, start + text.Length);
            text.CopyTo(_arguments.AsSpan(start));
            written = text.Length;
        }
        else if (value is ISpanFormattable)
        {
            // Optimized code (tiered JIT, Release, NativeAOT) elides the box this cast makes of a value
            // type, as the runtime's own interpolated-string handler relies on. Debug code boxes.
            while (!((ISpanFormattable)value).TryFormat(_arguments.AsSpan(start), out written, default, CultureInfo.InvariantCulture))
            {
                Reserve(ref _arguments, Math.Max(start + 16, _arguments.Length * 2));
            }
        }
        else
        {
            string formatted = value?.ToString() ?? string.Empty;
            Reserve(ref _arguments, start + formatted.Length);
            formatted.CopyTo(_arguments.AsSpan(start));
            written = formatted.Length;
        }

        _argumentEnds[_argumentCount++] = start + written;
    }

    // The key value is or holds: a TextKey, a TextKey? with a value, or an object boxing one. The JIT keeps
    // only T's branch, and reads a key of either value type without boxing it.
    private static bool TryGetKey<T>(ref T value, out TextKey key)
    {
        if (typeof(T) == typeof(TextKey))
        {
            key = Unsafe.As<T, TextKey>(ref value);

            return true;
        }

        if (typeof(T) == typeof(TextKey?))
        {
            TextKey? held = Unsafe.As<T, TextKey?>(ref value);
            key = held.GetValueOrDefault();

            return held.HasValue;
        }

        if (!typeof(T).IsValueType && value is TextKey boxed)
        {
            key = boxed;

            return true;
        }

        key = default;

        return false;
    }

    // Grows buffer to at least length, keeping what it holds.
    private static void Reserve(ref char[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            Array.Resize(ref buffer, Math.Max(length, buffer.Length * 2));
        }
    }

    // The key's encoded text in the shipped language closest to tag, and the language whose plural rules it takes.
    private static ReadOnlySpan<byte> Template(TextKey key, string? tag, out int language)
    {
        StringTable table = key.Table;
        language = table.Resolve(tag);
        ReadOnlySpan<byte> template = table.Template(language, key.Index);

        // A slot the language lacks holds the source text, whose plurals take the source language's rules.
        if (!template.IsEmpty && template[0] == StringTable.SourceText)
        {
            template = template[1..];
            language = 0;
        }

        return template;
    }

    // Decodes template into buffer from length on, and returns the new length. A plural block writes the
    // branch its count selects, in which each count mark writes the text of argument counted.
    private int DecodeTemplate(StringTable table, int language, ReadOnlySpan<byte> template, string? tag, int counted, ref char[] buffer, int length)
    {
        while (true)
        {
            int marker = template.IndexOfAny(StringTable.Placeholder, StringTable.PluralOpen, StringTable.Count);
            ReadOnlySpan<byte> literal = marker < 0 ? template : template[..marker];

            // A UTF-8 byte decodes to at most one char.
            Reserve(ref buffer, length + literal.Length);
            _ = Utf8.ToUtf16(literal, buffer.AsSpan(length), out _, out int decoded);
            length += decoded;
            if (marker < 0)
            {
                return length;
            }

            byte code = template[marker];
            if (code == StringTable.Count)
            {
                length = DecodeArgument(counted, tag, ref buffer, length);
                template = template[(marker + 1)..];
                continue;
            }

            int argument = template[marker + 1] - '0';
            template = template[(marker + 2)..];
            if (code == StringTable.Placeholder)
            {
                length = DecodeArgument(argument, tag, ref buffer, length);
                continue;
            }

            int close = template.IndexOf(StringTable.PluralClose);
            ulong magnitude = _counts[argument];
            ReadOnlySpan<byte> branch = StringTable.Branch(
                template[..close], magnitude, (_negatives & (1 << argument)) != 0, table.Category(language, magnitude));
            length = DecodeTemplate(table, language, branch, tag, argument, ref buffer, length);
            template = template[(close + 1)..];
        }
    }

    // Copies argument's formatted text into buffer at length, or decodes a key argument's text in the
    // shipped language closest to tag, and returns the new length.
    private int DecodeArgument(int argument, string? tag, ref char[] buffer, int length)
    {
        if ((_keyArguments & (1 << argument)) != 0)
        {
            TextKey key = _argumentKeys[argument];
            ReadOnlySpan<byte> template = Template(key, tag, out int language);

            return DecodeTemplate(key.Table, language, template, tag, counted: -1, ref buffer, length);
        }

        int start = argument == 0 ? 0 : _argumentEnds[argument - 1];
        int count = _argumentEnds[argument] - start;
        Reserve(ref buffer, length + count);
        _arguments.AsSpan(start, count).CopyTo(buffer.AsSpan(length));

        return length + count;
    }

    // The end of each argument's text in _arguments, one per placeholder a key can take.
    [InlineArray(3)]
    private struct ArgumentEnds
    {
        private int _end;
    }

    // Each argument that is a key, marked in _keyArguments.
    [InlineArray(3)]
    private struct ArgumentKeys
    {
        private TextKey _key;
    }

    // The magnitude of each integer argument, the count a plural form selects on. _negatives holds the signs.
    [InlineArray(3)]
    private struct ArgumentCounts
    {
        private ulong _count;
    }
}
