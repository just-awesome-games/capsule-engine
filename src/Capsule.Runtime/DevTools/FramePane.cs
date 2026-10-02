using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// One host frame in wall-clock milliseconds, and the fixed steps it ran.
internal readonly record struct FrameSample(double IntervalMs, double UpdateMs, double DrawMs, int Steps);

// One published second. Every field is zero until a whole second has been pushed.
internal readonly record struct FrameFigures(
    double Fps,
    double FrameMs,
    double WorstMs,
    double UpdateMs,
    double DrawMs,
    double StepsPerSecond);

// The last whole second in fixed-width fields. Allocation-free once on, or it skews its GC counts.
internal sealed class FramePane : ScreenEntity
{
    private const double SecondMs = 1000.0;

    private readonly ColorRect _backdrop;
    private readonly Label _label;

    // Sized past the widest layout. A figure too wide for its field widens the pane for that second.
    private readonly char[] _text = new char[256];

    // The second in progress, published when the intervals reach a second.
    private double _sumMs;
    private double _maxMs;
    private double _updateMs;
    private double _drawMs;
    private int _count;
    private int _steps;

    internal FrameFigures Figures { get; private set; }

    internal FramePane()
        : base(Anchor.TopRight, Vector2.Zero)
    {
        _backdrop = new ColorRect(Vector2.Zero) { Color = OverlayScene.BackdropColor };
        _label = new Label(OverlayScene.Font);

        Add(_backdrop);
        Add(_label);

        Reset();
    }

    internal string Text => _label.Text;

    // A pane switched back on does not join samples from before it was off.
    internal void Reset()
    {
        StartSecond();
        Publish(0, 0, 0, 0, 0);
    }

    internal void Push(in FrameSample sample)
    {
        _sumMs += sample.IntervalMs;
        _maxMs = Math.Max(_maxMs, sample.IntervalMs);
        _updateMs += sample.UpdateMs;
        _drawMs += sample.DrawMs;
        _count++;
        _steps += sample.Steps;
        if (_sumMs < SecondMs)
        {
            return;
        }

        double averageMs = _sumMs / _count;
        Publish(averageMs, _maxMs, _updateMs / _count, _drawMs / _count, _steps * SecondMs / _sumMs);
        StartSecond();
    }

    private void StartSecond()
    {
        _sumMs = _maxMs = _updateMs = _drawMs = 0;
        _count = _steps = 0;
    }

    // The GC counts and the heap are read here, at the second's end.
    private void Publish(double frameMs, double worstMs, double updateMs, double drawMs, double stepsPerSecond)
    {
        double fps = frameMs > 0 ? SecondMs / frameMs : 0;
        double heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        Figures = new FrameFigures(fps, frameMs, worstMs, updateMs, drawMs, stepsPerSecond);

        NumberText line = new(_text);
        line.Add("fps ");
        line.Add(fps, "F1", 6);
        line.Add("   frame ");
        line.Add(frameMs, "F2", 6);
        line.Add(" ms  max ");
        line.Add(worstMs, "F2", 6);
        line.Add("\nupdate ");
        line.Add(updateMs, "F2", 6);
        line.Add(" ms   draw ");
        line.Add(drawMs, "F2", 6);
        line.Add(" ms\nsteps ");
        line.Add(stepsPerSecond, "F1", 6);
        line.Add("/s   gc ");
        line.Add(GC.CollectionCount(0), 5);
        line.Add("/");
        line.Add(GC.CollectionCount(1), 3);
        line.Add("/");
        line.Add(GC.CollectionCount(2), 2);
        line.Add("   heap ");
        line.Add(heapMb, "F2", 7);
        line.Add(" MB");

        ReadOnlySpan<char> text = line.Written;
        _label.SetText(text);

        const int padding = OverlayScene.Padding;
        Vector2 measured = OverlayScene.Font.Measure(text);
        float width = (int)measured.X + (padding * 2);
        _backdrop.Offset = new Vector2(-width, 0f);
        _backdrop.Size = new Vector2(width, measured.Y + (padding * 2));
        _label.Offset = new Vector2(padding - width, padding);
    }
}
