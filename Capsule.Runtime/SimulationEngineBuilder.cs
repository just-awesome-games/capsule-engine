namespace Capsule.Runtime;

/// <summary>
/// The engine configured without a game's scene registry, so <see cref="EngineBuilder{TBuilder}.Run"/>
/// with a game's own <see cref="ISimulation"/> is the only way to start it. A game that declares
/// scenes boots through the generated <c>Capsule.Runtime.Generated.GameBoot</c> instead and gets a
/// <see cref="SceneEngineBuilder"/>, which can also run a scene.
/// </summary>
public sealed class SimulationEngineBuilder : EngineBuilder<SimulationEngineBuilder>
{
    internal SimulationEngineBuilder(string gameName)
        : base(gameName)
    {
    }
}
