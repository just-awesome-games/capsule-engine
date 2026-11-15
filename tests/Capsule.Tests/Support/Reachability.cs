namespace Capsule.Tests;

// Asks whether objects a spec expects released are still reachable. The spec takes the references in a
// method that is not inlined, which leaves nothing on its own stack to keep a target alive.
internal static class Reachability
{
    internal static void AssertReleased(IEnumerable<WeakReference> released)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.All(released, reference => Assert.False(reference.IsAlive, $"{reference.Target} is still held."));
    }
}
