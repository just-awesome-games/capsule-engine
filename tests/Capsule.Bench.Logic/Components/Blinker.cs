using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Components;

// Tints its renderer on and off on a period, as a game marks an invulnerable actor.
public sealed class Blinker(SpriteRenderer renderer, int period) : Component
{
    private static readonly ColorRgba Lit = new(255, 255, 255, 128);

    protected override void OnStep(in StepContext context) =>
        renderer.Color = (context.Tick / period) % 2 == 0 ? ColorRgba.White : Lit;
}
