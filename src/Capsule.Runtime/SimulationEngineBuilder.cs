namespace Capsule.Runtime;

/// <summary>Host configuration for a directly supplied <see cref="ISimulation"/>.</summary>
public sealed class SimulationEngineBuilder : EngineBuilder<SimulationEngineBuilder>
{
    internal SimulationEngineBuilder(string gameName)
        : base(gameName)
    {
    }
}
