using System.Runtime.InteropServices;
using Capsule.Input;

namespace Capsule.Runtime.Input;

// The snapshot every fixed step consumed, held until the run ends and then written as tape text.
// Owned by the builder and reached only through WithInputRecording, so a run that did not ask for
// one pays a null check per step and nothing else.
internal sealed class InputRecorder(string path) : IDisposable
{
    private readonly List<DeviceSnapshot> _steps = [];

    internal void Record(in DeviceSnapshot snapshot) => _steps.Add(snapshot);

    // Written on the way out rather than per step, so a run that crashes still leaves the tape of
    // everything that reached the simulation.
    public void Dispose() =>
        File.WriteAllText(path, InputTape.Of(CollectionsMarshal.AsSpan(_steps)).ToText());
}
