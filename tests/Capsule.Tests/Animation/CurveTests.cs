using Capsule.Animation;
using Capsule.Rendering;

namespace Capsule.Tests.Animation;

public sealed class CurveTests
{
    [Fact]
    public void Evaluate_HoldsTheFirstAndLastKeyOutsideTheirTimes()
    {
        Curve curve = Curve.FromKeys([new CurveKey(0.25f, 1f), new CurveKey(0.75f, 3f)]);

        Assert.Equal(1f, curve.Evaluate(0f));
        Assert.Equal(3f, curve.Evaluate(1f));
    }

    [Fact]
    public void Evaluate_IsLinearBetweenKeys()
    {
        Curve curve = Curve.Linear(0f, 10f);

        Assert.Equal(5f, curve.Evaluate(0.5f));
    }

    [Fact]
    public void Evaluate_AppliesEaseToTheFractionBetweenKeys()
    {
        Curve linear = Curve.Linear(0f, 1f);
        Curve eased = Curve.Eased(0f, 1f, Ease.InQuad);

        Assert.NotEqual(linear.Evaluate(0.5f), eased.Evaluate(0.5f));
        Assert.Equal(Easing.Apply(Ease.InQuad, 0.5f), eased.Evaluate(0.5f));
    }

    [Fact]
    public void Default_ReadsZeroEverywhere()
    {
        Curve curve = default;

        Assert.Equal(0f, curve.Evaluate(0f));
        Assert.Equal(0f, curve.Evaluate(0.5f));
        Assert.Equal(0f, curve.Evaluate(1f));
    }

    [Fact]
    public void Gradient_EvaluateLerpsBetweenStops()
    {
        Gradient gradient = Gradient.Linear(ColorRgba.Black, ColorRgba.White);

        Assert.Equal(ColorRgba.Lerp(ColorRgba.Black, ColorRgba.White, 0.5f), gradient.Evaluate(0.5f));
    }

    [Fact]
    public void Gradient_Default_ReadsWhite()
    {
        Gradient gradient = default;

        Assert.Equal(ColorRgba.White, gradient.Evaluate(0.5f));
    }
}
