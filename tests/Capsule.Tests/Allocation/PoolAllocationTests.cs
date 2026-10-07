using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

// A preloaded pool's entities attach, touch, leave and return without growing anything the scene holds. The
// scene reserved their room when it collected its preloads.
[Collection(StageAllocationCollection.Name)]
public sealed class PoolAllocationTests
{
    private const double StepSeconds = 1.0 / 60.0;
    private const int Pooled = 12;
    private const int FirstWave = 4;
    private const int Lifetime = 4;
    private const int WaveGap = 8;

    // One entity is out when the scene pool grows to five. Its four idle peers fill a stack doubled to
    // four, so its return needs the room the reservation made.
    private const int Lingering = 5;

    [Fact]
    public void APreloadedPoolsFirstWaves_AllocateNothingOnTheStepPath()
    {
        // A first scene runs every code path once. Only the second, whose collections start empty, is measured.
        Measure(out _, out _);
        StepSample[] samples = Measure(out Spawner spawner, out Lingerer lingerer);

        Assert.Equal(Pooled * 2, spawner.Contacts);
        Assert.True(lingerer.Left);
        Assert.Equal(Pooled, spawner.Probes.Available);
        long bytes = samples.Sum(static sample => sample.StepBytes + sample.ViewBytes);
        Assert.True(bytes == 0, $"The pool's first waves allocated {bytes} bytes. Per step and view: {string.Join(", ", samples.Select(static sample => $"{sample.StepBytes}+{sample.ViewBytes}"))}");
    }

    private static StepSample[] Measure(out Spawner spawner, out Lingerer lingerer)
    {
        Scene scene = new();
        scene.Add(new Wall());
        lingerer = scene.Pool<Lingerer>().Take();
        scene.Add(lingerer);
        spawner = new();
        scene.Add(spawner);
        using SceneSimulation simulation = new(scene, run: StageWorkload.Defaults);

        return StepMeasurement.Measure(simulation, StepSeconds, warmupSteps: FirstWave - 1, measuredSteps: WaveGap * 3);
    }

    private sealed class Wall : Entity
    {
        internal Wall()
            : base(Vector2.Zero) => Add(new BoxCollider2D(new Vector2(64f, 64f)) { Layer = "solid" });
    }

    // Takes the whole pool after the scene starts, and again once it has returned. Every probe lands on the wall.
    private sealed class Spawner : Entity
    {
        internal EntityPool<Probe> Probes { get; } = new(static () => new Probe(), Pooled);

        internal int Contacts { get; set; }

        internal Spawner()
            : base(Vector2.Zero)
        {
        }

        protected internal override void CollectAssets(AssetCollection assets)
        {
            Probes.CollectAssets(assets);
            assets.Pool<Lingerer>(Lingering);
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (context.Tick != FirstWave && context.Tick != FirstWave + WaveGap)
            {
                return;
            }

            for (int index = 0; index < Pooled; index++)
            {
                Scene.Add(Probes.Take().Launch(this, new Vector2(index * 4f, 0f), context.Tick));
            }
        }
    }

    // Taken from the scene pool before the scene preloads, and returned to it mid-run.
    private sealed class Lingerer() : Entity(Vector2.Zero)
    {
        internal bool Left { get; private set; }

        protected internal override void OnStep(in StepContext context)
        {
            if (context.Tick == FirstWave + 1)
            {
                Left = true;
                Scene.Remove(this);
            }
        }
    }

    private sealed class Probe : Entity
    {
        // Its own layer and two of those it detects are new to the world, which interns them on its first attach.
        private readonly BoxCollider2D _box = new(new Vector2(8f, 8f)) { Layer = "probe", ReportsContacts = true };
        private Spawner? _spawner;
        private long _launched;
        private bool _touched;

        internal Probe()
            : base(Vector2.Zero)
        {
            _box.Detects = new("solid", "hazard", "pickup");
            Add(_box);
            Add(new VisibleOnScreenNotifier2D(new Vector2(8f, 8f)));
        }

        internal Probe Launch(Spawner spawner, Vector2 position, long tick)
        {
            _spawner = spawner;
            _launched = tick;
            _touched = false;
            Position = position;
            return this;
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (_box.Touching.Length > 0 && !_touched)
            {
                _touched = true;
                _spawner!.Contacts++;
            }

            if (context.Tick >= _launched + Lifetime)
            {
                Scene.Remove(this);
            }
        }
    }
}
