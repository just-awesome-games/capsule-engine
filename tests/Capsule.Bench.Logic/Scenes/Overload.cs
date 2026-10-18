namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// Steps that each cost a few step lengths of arithmetic, which no frame can keep pace with. The
/// frame interval it keeps is the catch-up policy's under sustained overload, about one step's cost.
/// </summary>
[Workload(WorkloadKind.Paced)]
public sealed class Overload : Scene
{
    private const int RoundsPerStep = 50_000_000;

    private ulong _state = 1;

    protected override void OnStep(in StepContext context)
    {
        ulong state = _state;
        for (int round = 0; round < RoundsPerStep; round++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
        }

        _state = state;
    }
}
