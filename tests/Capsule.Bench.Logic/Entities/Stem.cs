namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// A post that sheds its <see cref="Bud"/> and parents a spare in its place every <c>period</c> steps, on the
/// same step as every other stem. The shed bud leaves the scene and the spare joins it as a child.
/// </summary>
public sealed class Stem : Entity
{
    private readonly int _period;
    private Bud _shown;
    private Bud _spare = new();

    public Stem(Vector2 position, int period)
        : base(position)
    {
        _period = period;
        _shown = new Bud { Parent = this };
    }

    protected override void OnStep(in StepContext context)
    {
        if (context.Tick % _period != 0)
        {
            return;
        }

        Scene!.Remove(_shown);
        (_shown, _spare) = (_spare, _shown);
        _shown.Parent = this;
    }
}
