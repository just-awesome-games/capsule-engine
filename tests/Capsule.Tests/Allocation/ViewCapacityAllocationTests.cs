using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

// A host lends each scene the view the scene before it returned, which keeps the capacity it grew.
[Collection(StageAllocationCollection.Name)]
public sealed class ViewCapacityAllocationTests(ITestOutputHelper output)
{
    // A scene after a busier one draws into the view the busier one grew.
    [Fact]
    public void ASceneAfterABusierOne_BuildsItsFirstFrameWithoutAllocating()
    {
        using SceneHost host = new(SceneTransition.ToScene(typeof(Busy), null), Resolve, new Run());
        host.Step(SceneFixtures.Step(0));
        _ = host.View;

        host.Run.RequestScene<Quiet>();
        host.Step(SceneFixtures.Step(1));
        Assert.IsType<Quiet>(host.Scene);

        long before = StepMeasurement.Start();
        int drawn = host.View.Sprites.Length;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        StepMeasurement.Report(output, "the first frame after a busier scene", bytes, steps: 1);
        Assert.Equal(Quiet.Sprites, drawn);
    }

    // Two simulations drawing at once borrow a view each. The one returned at a disposal goes to the next
    // borrower, and a view lent out goes to nobody else.
    [Fact]
    public void SimulationsDrawingAtOnce_BorrowDistinctViews()
    {
        ViewLender lender = new();
        SceneSimulation first = new(new Quiet(), null, new Run(), lender);
        using SceneSimulation second = new(new Quiet(), null, new Run(), lender);
        FrameView firstView = first.View;

        Assert.NotSame(firstView, second.View);

        first.Dispose();
        using SceneSimulation third = new(new Quiet(), null, new Run(), lender);

        Assert.Same(firstView, third.View);
        Assert.NotSame(second.View, third.View);
    }

    private static Scene Resolve(in SceneTransition target) => target.SceneType == typeof(Quiet) ? new Quiet() : new Busy();

    private sealed class Busy : Scene
    {
        public Busy()
        {
            SceneFixtures.Open(this, SceneFixtures.Viewport / 2f, SceneFixtures.Viewport);
            Add(new Crowded(SceneFixtures.Viewport / 2f, Quiet.Sprites * 2));
        }
    }

    private sealed class Quiet : Scene
    {
        internal const int Sprites = 1_000;

        public Quiet()
        {
            SceneFixtures.Open(this, SceneFixtures.Viewport / 2f, SceneFixtures.Viewport);
            Add(new Crowded(SceneFixtures.Viewport / 2f, Sprites));
        }
    }

    private sealed class Crowded : Entity
    {
        public Crowded(Vector2 position, int count)
            : base(position) => Add(new Crowd(count));
    }

    // Count 1x1 sprites at its entity.
    private sealed class Crowd(int count) : Renderer
    {
        private readonly Sprite _frame = SceneFixtures.Frame(1, 1);

        internal override bool Steps => false;

        protected internal override void Draw(FrameView view)
        {
            Vector2 at = Entity!.Position;
            for (int index = 0; index < count; index++)
            {
                view.Add(new SpriteIntent(_frame, at, at, 0f, 0f, Vector2.One, false, false, ColorRgba.White));
            }
        }
    }
}
