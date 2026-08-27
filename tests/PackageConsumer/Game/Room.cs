using Capsule;
using Capsule.Scenes;

namespace PackageConsumer.Game;

/// <summary>The scene claiming the <c>room</c> map: terrain, one marker, and a camera on it.</summary>
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
