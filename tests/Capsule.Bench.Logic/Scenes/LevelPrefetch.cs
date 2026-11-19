using Capsule.Bench.Logic.Cameras;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// A lobby that prefetches <see cref="AuthoredLevel"/> at step <see cref="PrefetchStep"/> and opens it at step
/// <see cref="RequestStep"/>, which times what a level costs the frames around its boundary.
/// </summary>
/// <remarks>
/// Row <c>LevelCold</c> opens it with no prefetch. The rows ending in <c>Huge</c> open
/// <c>authored-huge.scene.json</c> instead, ten times the level, which <c>AuthoredHuge.targets</c> writes on the
/// first build. The signal is <c>updateMs</c> max, and the frame CSV's <c>updateMs</c> on the frames after each
/// of the two steps. The <c>LevelSwap</c> driver adds the collections and pause time from the prefetch to the
/// boundary.
/// </remarks>
[Workload(WorkloadKind.Paced)]
[Workload(WorkloadKind.Paced, Row = Cold)]
[Workload(WorkloadKind.Paced, Row = Huge)]
[Workload(WorkloadKind.Paced, Row = ColdHuge)]
public sealed class LevelPrefetch : Scene
{
    public const int PrefetchStep = 120;

    public const int RequestStep = 150;

    private const string Cold = "LevelCold";
    private const string Huge = "LevelPrefetchHuge";
    private const string ColdHuge = "LevelColdHuge";

    private readonly bool _prefetches = Workloads.Running is not (Cold or ColdHuge);

    private readonly SceneKey _level = Workloads.Running is Huge or ColdHuge
        ? CapsuleAssets.Scenes.AuthoredHugeScene
        : CapsuleAssets.Scenes.AuthoredLevelScene;

    public LevelPrefetch() =>
        Camera = new ParkedCamera();

    protected override void OnStep(in StepContext context)
    {
        if (context.Tick == PrefetchStep && _prefetches)
        {
            Run.PrefetchScene(_level);
        }
        else if (context.Tick == RequestStep)
        {
            Run.RequestScene(_level);
        }
    }
}
