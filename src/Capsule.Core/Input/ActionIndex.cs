namespace Capsule.Input;

// The dense index every action name is resolved to once. A step then reads a binding out of an array
// instead of hashing a string. Indices start at 1, leaving 0 for an unnamed action, and are never reused.
// A name interned here stays interned for the process, so construct an action once and keep it instead of
// building one per step.
internal static class ActionIndex
{
    private static readonly Lock Gate = new();

    private static readonly Dictionary<string, int> Indices = new(StringComparer.Ordinal);

    internal static int Of(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        lock (Gate)
        {
            if (Indices.TryGetValue(name, out int index))
            {
                return index;
            }

            index = Indices.Count + 1;
            Indices.Add(name, index);

            return index;
        }
    }
}
