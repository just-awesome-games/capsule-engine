using Capsule.Scenes;
using Capsule.Scenes.Generated;

namespace Capsule.Bench.Logic;

/// <summary>Every scene this assembly registers, in registry order: a scene under <c>Scenes/</c> is a workload the moment it compiles.</summary>
public static class Workloads
{
    public static IReadOnlyList<Type> All { get; } = Array.ConvertAll(CapsuleScenes.Registrations, static registration => registration.SceneType);
}
