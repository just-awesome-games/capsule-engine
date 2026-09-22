using Capsule.Scenes;

namespace Capsule.Bench.Logic;

/// <summary>Every scene class this assembly registers, in registry order: a scene under <c>Scenes/</c> is a workload the moment it compiles.</summary>
public static class Workloads
{
    public static IReadOnlyList<Type> All { get; } = Build();

    private static Type[] Build()
    {
        List<Type> classes = [];
        foreach (SceneRegistration registration in CapsuleScenes.Registrations)
        {
            if (registration.SceneType is { } sceneType)
            {
                classes.Add(sceneType);
            }
        }

        return [.. classes];
    }
}
