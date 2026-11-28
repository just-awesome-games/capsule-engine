using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Tests;

// Steps by a draw from the run's random source, and a unit down in a step that presses nudge. A position
// holds only if the whole run replays.
internal sealed class Wanderer(InputAction? nudge = null) : Entity(Vector2.Zero)
{
    protected internal override void OnStep(in StepContext context) =>
        Teleport(Position + new Vector2(Random.Range(-1f, 1f), nudge is { } pressed && context.Input.WasPressed(pressed) ? 1f : 0f));
}
