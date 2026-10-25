namespace Capsule.Tests;

// SDL's subsystems, event queue and event watches are process-wide: a test that starts a subsystem or
// pushes an event cannot run beside another that does.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SdlEventsCollection
{
    internal const string Name = "sdl-events";
}
