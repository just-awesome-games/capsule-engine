using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class SpriteRotationTests
{
    private static readonly TextureHandle Atlas = new("atlas", ".png");

    // A pair straddling the wrap turns the short way across it, not the long way round through
    // zero: from just short of pi to just past -pi is a small clockwise turn.
    [Fact]
    public void Interpolation_CrossesTheWrapTheShortWay()
    {
        float previous = MathF.PI - 0.2f;
        float current = -MathF.PI + 0.2f;

        float midway = StepInterpolation.Interpolate(previous, current, 0.5f);

        Assert.Equal(MathF.PI, midway, 1e-5f);
    }

    [Fact]
    public void Interpolation_ReturnsCurrentExactlyAtEqualEndpoints()
    {
        const float angle = 2.7182818f;

        Assert.Equal(angle, StepInterpolation.Interpolate(angle, angle, 0.3f));
        Assert.Equal(angle, StepInterpolation.Interpolate(angle, angle, 1f / 3f));
    }

    [Fact]
    public void Interpolation_IsLinearAcrossAQuarterTurn()
    {
        const float quarter = MathF.PI / 2f;

        Assert.Equal(0f, StepInterpolation.Interpolate(0f, quarter, 0f));
        Assert.Equal(quarter / 4f, StepInterpolation.Interpolate(0f, quarter, 0.25f), 1e-6f);
        Assert.Equal(quarter, StepInterpolation.Interpolate(0f, quarter, 1f), 1e-6f);
    }

    // A whole extra turn in the written value is not a turn on screen: the arc is the wrapped
    // difference, so an angle that grew past a full turn interpolates as the small turn it is.
    [Fact]
    public void Interpolation_IgnoresWholeTurnsInTheDifference()
    {
        float previous = 0.1f;
        float current = 0.1f + MathF.Tau + 0.4f;

        Assert.Equal(0.3f, StepInterpolation.Interpolate(previous, current, 0.5f), 1e-5f);
    }

    // An 8x8 frame centred on its pivot and sitting just outside the camera's right edge: its
    // axis-aligned rect stops at the edge, but the circle it sweeps when turned reaches in.
    [Fact]
    public void AView_KeepsATurnedSpriteWhoseCircleReachesTheCamera_AndCullsTheSameSpriteUnturned()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)) };
        SpriteIntent unturned = Centred(new Vector2(14.5f, 5));
        SpriteIntent turned = unturned with { PreviousRotation = 0.5f, Rotation = 0.5f };

        view.Add(unturned);
        view.Add(turned);

        Assert.Equal(turned, Assert.Single(view.Sprites.ToArray()));
    }

    // Either end of the step turned is enough to widen the cull to the circle: a sprite settling
    // back to zero this step still swept its corners past the rect.
    [Fact]
    public void AView_CullsByTheCircleWhenOnlyThePreviousRotationIsTurned()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)) };
        SpriteIntent settling = Centred(new Vector2(14.5f, 5)) with { PreviousRotation = 0.5f, Rotation = 0f };

        view.Add(settling);

        Assert.Single(view.Sprites.ToArray());
    }

    // The circle is about the pivot, wherever the pivot is: at the same position and turn, an 8x8
    // frame anchored at its corner reaches its whole diagonal back over the camera's left edge,
    // where the same frame anchored at its centre reaches only half of it and stays outside.
    [Fact]
    public void AView_MeasuresTheCircleFromThePivot()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)) };
        SpriteIntent centred = Centred(new Vector2(-9f, 5)) with { PreviousRotation = 1f, Rotation = 1f };
        SpriteIntent cornered = centred with { Sprite = centred.Sprite with { Pivot = Vector2.Zero } };

        view.Add(centred);
        view.Add(cornered);

        Assert.Equal(cornered, Assert.Single(view.Sprites.ToArray()));
    }

    [Fact]
    public void AView_DrawsNothingForATiledSpriteThatIsTurned_AndCountsTheSubmission()
    {
        FrameView view = new();
        SpriteIntent turned = Centred(new Vector2(4, 4)) with { Rotation = 0.25f };

        view.Add(turned, new Vector2(64, 0));

        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(new RenderMetrics(Submitted: 1, Visible: 0), view.Metrics);
    }

    // Refused as a scale that is not a scale is: by the cull, so a frame with no camera to cull
    // against is the host's to refuse.
    [Fact]
    public void AView_DrawsNothingForANonFiniteRotation()
    {
        FrameView view = new() { Camera = new CameraView(new Vector2(5, 5), new Vector2(10, 10)) };

        view.Add(Centred(new Vector2(4, 4)) with { Rotation = float.NaN });
        view.Add(Centred(new Vector2(4, 4)) with { PreviousRotation = float.PositiveInfinity });

        Assert.Empty(view.Sprites.ToArray());
        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 0), view.Metrics);
    }

    private static SpriteIntent Centred(Vector2 position) =>
        new(
            new Sprite(Atlas, new TextureRegion(0, 0, 8, 8), new Vector2(4, 4)),
            position,
            position,
            PreviousRotation: 0f,
            Rotation: 0f,
            new Vector2(8, 8),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);
}
