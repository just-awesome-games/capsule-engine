namespace Capsule.Tests.Runtime;

// The installed log sink is one process-wide slot: a test asserting on its own sink cannot run
// beside one that composes an engine, which installs a sink of its own.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LogSinkCollection
{
    internal const string Name = "log-sink";
}
