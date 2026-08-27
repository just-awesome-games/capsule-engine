namespace Capsule.Tests.Performance;

/// <summary>
/// The specs that measure. They run alone: a step duration taken while another spec has the
/// other cores is a number about the runner rather than about the engine.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StagePerformanceCollection
{
    internal const string Name = "stage-performance";
}
