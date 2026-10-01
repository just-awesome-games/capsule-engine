namespace Capsule.Scenes;

// Builds the "Registered: a, b, c" tail a registry's not-found message ends with, shared by the scene, entity
// and input-driver registries. The names are sorted, so the message reads the same whatever order the registry
// was built in.
internal static class Registered
{
    internal static string Names<T>(IEnumerable<T> items)
    {
        string names = string.Join(", ", items.Select(static item => item?.ToString()).Order(StringComparer.Ordinal));
        return names.Length == 0 ? "nothing" : names;
    }
}
