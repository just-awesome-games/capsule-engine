using System.Globalization;
using Capsule.Bench.Logic.Scenes;
using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Bench;

/// <summary>The <see cref="LevelPrefetch"/> driver: presses nothing, and prints the collections and the pause time from the prefetch step until three steps after the boundary.</summary>
public sealed class LevelSwap : IInputDriver
{
    private int _gen0;
    private int _gen1;
    private int _gen2;
    private TimeSpan _paused;
    private long _swapped = -1;

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        if (tick == LevelPrefetch.PrefetchStep)
        {
            _gen0 = GC.CollectionCount(0);
            _gen1 = GC.CollectionCount(1);
            _gen2 = GC.CollectionCount(2);
            _paused = GC.GetTotalPauseDuration();
        }
        else if (_swapped < 0 && tick > LevelPrefetch.PrefetchStep && scene is not LevelPrefetch)
        {
            _swapped = tick;
        }
        else if (_swapped >= 0 && tick == _swapped + 3)
        {
            double pauseMs = (GC.GetTotalPauseDuration() - _paused).TotalMilliseconds;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"#bench level-swap swappedAt={_swapped} gen0={GC.CollectionCount(0) - _gen0} gen1={GC.CollectionCount(1) - _gen1} gen2={GC.CollectionCount(2) - _gen2} pauseMs={pauseMs:F3}"));
        }

        return true;
    }
}
