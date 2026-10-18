using Capsule.Scenes;

namespace Capsule.Rendering;

// The screen notifiers of one scene and the frame region they settle against.
internal sealed class SceneVisibility
{
    private readonly Scene _scene;
    private readonly SettleList<VisibleOnScreenNotifier2D> _notifiers = new();

    // Notifiers that joined between steps, settled as the next step begins.
    private readonly List<VisibleOnScreenNotifier2D> _arrivals = [];

    // The frame's visible region, held so notifiers settle against it.
    private Rect _region;

    internal SceneVisibility(Scene scene)
    {
        _scene = scene;
    }

    internal int Count => _notifiers.Count;

    // A notifier joining between steps waits for the next step to begin. One landing with a step's deferred adds
    // settles after the drain.
    internal void Track(VisibleOnScreenNotifier2D notifier)
    {
        _notifiers.Add(notifier);
        if (!_scene.Stepping)
        {
            _arrivals.Add(notifier);
        }
    }

    internal void Untrack(VisibleOnScreenNotifier2D notifier)
    {
        _notifiers.Remove(notifier);
        _arrivals.Remove(notifier);
    }

    internal void Frame(Rect region) => _region = region;

    // Notifiers that joined between steps settle against the last frame before any entity steps. Each first step
    // reads that frame as one landing with a step's deferred adds does. A held one waits for the step its entity
    // first runs. The walk restarts after each settle, as a handler may detach another.
    internal void SettleArrivals()
    {
        for (int index = 0; index < _arrivals.Count;)
        {
            VisibleOnScreenNotifier2D notifier = _arrivals[index];
            if (notifier.Entity!.Held)
            {
                index++;
                continue;
            }

            _arrivals.RemoveAt(index);
            notifier.SettleVisibility(_region);
            index = 0;
        }
    }

    // Settle registered notifiers against the frame region. One registered during the walk waits
    // for the next step.
    internal void Settle()
    {
        _notifiers.Begin();
        try
        {
            while (_notifiers.TryNext(out VisibleOnScreenNotifier2D? notifier))
            {
                if (!notifier.Entity!.Held)
                {
                    notifier.SettleVisibility(_region);
                }
            }
        }
        finally
        {
            _notifiers.End();
        }
    }

    internal void Reserve(int notifiers) => _notifiers.Reserve(notifiers);

    internal void Clear() => _notifiers.Clear();
}
