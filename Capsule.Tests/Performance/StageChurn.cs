namespace Capsule.Tests.Performance;

/// <summary>What changes structurally while the stage runs, which is what separates one measurement from another.</summary>
internal enum StageChurn
{
    /// <summary>Nothing joins or leaves, so the draw list is built once and never rebuilt.</summary>
    None,

    /// <summary>One entity leaves and rejoins on alternating steps: the draw list rebuilds every step and nothing else moves.</summary>
    DrawListOnly,

    /// <summary>Twenty spawns and twenty despawns a second, as a bullet-heavy game runs.</summary>
    Spawning,
}
