using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;

namespace Capsule.AotSmoke.Logic;

public static class FixtureInput
{
    public static readonly InputAction Quit = new("quit");

    public static void Configure(InputConfiguration input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Bindings.Bind(Quit, Key.Escape);
    }
}

[SceneDocument("fixture")]
public sealed class FixtureScene(SceneContent content) : Scene(content)
{
    protected override void OnStart() => Camera.ViewportSize = new Vector2(16f, 16f);

    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(FixtureInput.Quit))
        {
            RequestExit();
        }
    }
}

public sealed class FixtureEntity : Entity
{
    private static readonly Sprite Visual = new(CapsuleAssets.Textures.Pixel, new TextureRegion(0, 0, 1, 1));

    public FixtureEntity(EntitySpawn spawn)
        : base(spawn.Position)
    {
        Add(new SpriteRenderer(Visual));
    }
}
