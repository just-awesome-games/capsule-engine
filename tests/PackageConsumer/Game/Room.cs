using Capsule;
using Capsule.Scenes;

namespace PackageConsumer.Game;

[MapName("room")]
public sealed class Room : MapScene
{
    public Room(MapSceneContext context)
        : base(context)
    {
    }

    protected override void OnStart() => Camera.Teleport(FindSingle<Marker>().Position);

    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(ConsumerInput.Quit))
        {
            RequestExit();
        }
    }

    protected override void OnLateStep(in StepContext context) => Camera.Center = FindSingle<Marker>().Position;
}
