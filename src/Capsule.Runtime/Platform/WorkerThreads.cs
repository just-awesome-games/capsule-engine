namespace Capsule.Runtime.Platform;

// Whether the game thread may block on work queued to the thread pool. A browser or WASI build runs the
// pool on the game thread, where such a wait never returns.
internal static class WorkerThreads
{
    internal static readonly bool CanWaitOn = !OperatingSystem.IsBrowser() && !OperatingSystem.IsWasi();
}
