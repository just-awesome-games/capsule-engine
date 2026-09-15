namespace Capsule.Scenes.Lifecycle;

internal static class ReferenceList
{
    // Membership is reference identity: a subclass may override Equals, and two distinct instances
    // that compare equal must never stand in for each other here.
    internal static int IndexOf<T>(List<T> items, T item)
        where T : class
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }
}
