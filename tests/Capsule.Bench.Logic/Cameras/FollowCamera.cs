using System.Numerics;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Cameras;

public sealed class FollowCamera : Camera
{
    private Hero _subject = null!;

    public FollowCamera() => ViewportSize = new Vector2(320f, 180f);

    protected override void OnStart()
    {
        _subject = Scene!.FindSingle<Hero>();
        Teleport(_subject.Position);
    }

    protected override void OnLateStep(in StepContext context) => Center = _subject.Position;
}
