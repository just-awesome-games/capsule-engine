using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// One host frame as the overlay measured it: wall-clock milliseconds for the interval since the
// previous frame, the update bracket and the last game frame's draw submission, and the fixed steps the
// frame ran.
internal readonly record struct FrameSample(double IntervalMs, double UpdateMs, double DrawMs, int Steps);

// One second's figures as the pane published them, which its lines are formatted from. Every field is
// zero until a whole second has been pushed.
internal readonly record struct FrameFigures(
    double Fps,
    double FrameMs,
    double WorstMs,
    double UpdateMs,
    double DrawMs,
    double StepsPerSecond);

// The corner readout: a backdrop and a three-line label hanging from the canvas's top-right, showing
// the last whole second and rewritten when a second completes. Every figure sits in a fixed-width
// field, so the pane keeps one width. Allocation-free once on, since a pane that churned the GC would
// distort the counts it shows.
internal sealed class FramePane : ScreenEntity
{
    private const int Padding = 4;
    private const double SecondMs = 1000.0;

    private static readonly BitmapFont Font = BitmapFont.Default;

    private readonly ColorRect _backdrop;
    private readonly Label _label;

    // Sized past the widest layout the fields allow. A figure too wide for its field takes what it
    // needs and the pane grows for that second.
    private readonly char[] _text = new char[256];

    // The second in progress. Steps run and the update and draw brackets are summed alongside the
    // intervals, all published together when the intervals reach a second.
    private double _sumMs;
    private double _maxMs;
    private double _updateMs;
    private double _drawMs;
    private int _count;
    private int _steps;

    internal FramePane()
        : base(Anchor.TopRight, Vector2.Zero)
    {
        _backdrop = new ColorRect(Vector2.Zero) { Color = ColorRgba.Black with { A = 160 } };
        _label = new Label(Font);

        Add(_backdrop);
        Add(_label);

        Reset();
    }

    internal string Text => _label.Text;

    internal FrameFigures Figures { get; private set; }

    // Drops the second in progress and shows zeros until a whole second has been pushed. A pane
    // switched back on does not join samples from before it was off.
    internal void Reset()
    {
        _sumMs = 0;
        _maxMs = 0;
        _updateMs = 0;
        _drawMs = 0;
        _count = 0;
        _steps = 0;
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

        _sumMs = 0;
        _maxMs = 0;
        _updateMs = 0;
        _drawMs = 0;
        _count = 0;
        _steps = 0;
    }

    // Rewrites the label from one second's figures. The collection counts and the heap are read here,
    // at the second's end.
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

        Vector2 measured = Font.Measure(text);
        float width = (int)measured.X + (Padding * 2);
        _backdrop.Offset = new Vector2(-width, 0f);
        _backdrop.Size = new Vector2(width, measured.Y + (Padding * 2));
        _label.Offset = new Vector2(Padding - width, Padding);
    }

}
