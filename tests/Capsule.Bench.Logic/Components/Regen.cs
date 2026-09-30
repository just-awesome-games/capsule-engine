using Capsule.Scenes;

namespace Capsule.Bench.Logic.Components;

// A stat that climbs back to its cap a point a step, as a game's health or stamina does.
public sealed class Regen(int cap) : Component
{
    public int Value { get; set; }

    protected override void OnStep(in StepContext context)
    {
        if (Value < cap)
        {
            Value++;
        }
    }
}
