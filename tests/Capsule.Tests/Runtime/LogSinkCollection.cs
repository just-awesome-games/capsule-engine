namespace Capsule.Tests.Runtime;

// The installed log sink is one process-wide slot. A test that asserts on what its own sink
// collected cannot run beside one that composes an engine, because building a host installs a sink
// of its own and the lines go there instead.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LogSinkCollection
{
    internal const string Name = "log-sink";
}
