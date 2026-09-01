using Capsule;
using Capsule.Scenes;

namespace MinimalGame.Game;

[SceneDocument("room")]
public sealed class Room : Scene
{
    public Room(SceneContent content)
        : base(content)
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
