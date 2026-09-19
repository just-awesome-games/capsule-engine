namespace Capsule.Scenes;

// Builds the "Registered: a, b, c" tail a registry's not-found message ends with, shared by the scene, entity
// and input-driver registries. The names are sorted, so the message reads the same whatever order the registry
// was built in.
internal static class Registered
{
    internal static string Names<T>(ICollection<T> items)
    {
        if (items.Count == 0)
        {
            return "nothing";
        }

        string[] names = new string[items.Count];
        int next = 0;
        foreach (T item in items)
        {
            names[next++] = item?.ToString() ?? string.Empty;
        }

        Array.Sort(names, StringComparer.Ordinal);

        return string.Join(", ", names);
    }
}
