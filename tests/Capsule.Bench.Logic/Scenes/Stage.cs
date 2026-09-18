using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>The 512 by 64 corridor of <c>stage.scene.json</c> under a follow camera, with a spark spawned every third step: a big tile map, culling, placed entities and deferred adds and removes.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Stage : Scene
{
    private readonly Hero _hero;
    private int _sparked;

    public Stage(SceneContent content)
        : base(content)
    {
        Camera = new FollowCamera();
        _hero = FindSingle<Hero>();
    }

    protected override void OnStep(in StepContext context)
    {
        if (context.Tick % 3 == 0)
        {
            Add(new Spark(_hero.Position, new Vector2(4f, ((_sparked++ % 5) - 2) * 0.5f)));
        }
    }
}
