using Capsule.Build.Configuration;

namespace Capsule.Build.Clips;

/// <summary>
/// Reads the value clip document format. The clip is known at build time.
/// </summary>
/// <remarks>
/// The generated registry carries it as literal data, and no document ships or is parsed at run time.
/// </remarks>
internal static class ClipFile
{
    /// <summary>Reads and validates the clip at <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">The JSON is malformed or the document breaks the format.</exception>
    internal static Clip Read(string path) =>
        Validate(StrictJson.Read(path, ClipJsonContext.Default.ClipJson, ClipJson.Members, nullFix: null, ClipJson.Shape));

    private static Clip Validate(ClipJson json)
    {
        if (json.FormatVersion is not { } format)
        {
            throw new FormatException($"has no formatVersion. This build supports formatVersion {ClipJson.SupportedFormat}. Set \"formatVersion\": {ClipJson.SupportedFormat}.");
        }

        if (format != ClipJson.SupportedFormat)
        {
            throw new FormatException(
                $"declares formatVersion {format}, which is unsupported. This build supports formatVersion {ClipJson.SupportedFormat}. Write the clip in that version.");
        }

        if (json.Keys is not { Count: >= ClipJson.NonEmpty } declared)
        {
            throw new FormatException("has no keys. A clip plays at least one. Add a key to \"keys\", as { \"value\": 1, \"ticks\": 8 }.");
        }

        ClipKey[] keys = new ClipKey[declared.Count];
        long total = 0;
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i] is not { } key)
            {
                throw new FormatException($"has keys[{i}] as null. Every key is an object. Write the key, or remove the null.");
            }

            if (key.Value is not { } value)
            {
                throw new FormatException($"has keys[{i}] with no value. Every key holds a number. Set its \"value\".");
            }

            // A number past float's range reads as an infinity.
            if (!float.IsFinite(value))
            {
                throw new FormatException($"has keys[{i}] with a value past the range of a float. Set its \"value\" to a smaller number.");
            }

            if (key.Ticks is not { } ticks)
            {
                throw new FormatException($"has keys[{i}] with no ticks. Every key lasts a whole number of fixed steps. Set its \"ticks\" to {ClipJson.LeastTicks} or more.");
            }

            if (ticks < ClipJson.LeastTicks)
            {
                throw new FormatException($"has keys[{i}] lasting {ticks} ticks. Every key lasts at least {ClipJson.LeastTicks} fixed step. Set its \"ticks\" to {ClipJson.LeastTicks} or more.");
            }

            total += ticks;
            keys[i] = new ClipKey(value, ticks);
        }

        if (total > int.MaxValue)
        {
            throw new FormatException($"has keys lasting {total} ticks in all, past the {int.MaxValue} a clip holds. Shorten the keys or split the clip.");
        }

        return new Clip(json.Loop ?? false, json.Blend ?? ClipBlend.Step, keys);
    }
}
