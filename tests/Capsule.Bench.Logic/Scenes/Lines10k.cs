using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 world lines a frame: the line path alone.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Lines10k : Scene
{
    public Lines10k()
    {
        Camera = new ParkedCamera();
        Add(new Holder(new LineField()));
    }
}
