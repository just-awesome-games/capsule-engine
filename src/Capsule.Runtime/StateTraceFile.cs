using Capsule.Scenes;

namespace Capsule.Runtime;

// Writes a run's StateTrace out once, on the way out. Owned by the builder and reached only
// through WithStateTrace, so a run that did not ask for one never constructs it.
internal sealed class StateTraceFile(string path, StateTrace trace) : IDisposable
{
    internal StateTrace Trace { get; } = trace;

    // Written at the end rather than per step, so a run that crashes still leaves the trace of
    // every step it ran.
    public void Dispose()
    {
        using StreamWriter writer = new(path, append: false);
        Trace.Write(writer);
    }
}
