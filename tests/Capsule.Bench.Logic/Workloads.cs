using Capsule.Scenes;

namespace Capsule.Bench.Logic;

/// <summary>
/// Every top-level scene class this assembly registers, in registry order: a scene under <c>Scenes/</c>
/// is a workload the moment it compiles. A nested scene is a part of the workload that declares it.
/// </summary>
public static class Workloads
{
    public static IReadOnlyList<Type> All { get; } = Build();

    private static Type[] Build()
    {
        List<Type> classes = [];
        foreach (SceneRegistration registration in CapsuleScenes.Registrations)
        {
            if (registration.SceneType is { IsNested: false } sceneType)
            {
                classes.Add(sceneType);
            }
        }

        return [.. classes];
    }
}
