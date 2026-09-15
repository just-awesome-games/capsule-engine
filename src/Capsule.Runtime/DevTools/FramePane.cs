using System.Globalization;
using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.DevTools;

// One host frame as the overlay measured it: wall-clock milliseconds for the interval since the
// previous frame, the update bracket and the last game frame's draw submission, and the fixed
// steps the frame ran.
internal readonly record struct FrameSample(double IntervalMs, double UpdateMs, double DrawMs, int Steps);

// One second's figures as the pane published them, which is what its lines are formatted from;
// every field zero until a whole second has been pushed.
internal readonly record struct FrameFigures(
    double Fps,
    double FrameMs,
    double WorstMs,
    double UpdateMs,
    double DrawMs,
    double StepsPerSecond);

// The corner readout: a backdrop and one three-line label hanging from the canvas's top-right,
// showing the last whole second and rewritten only when a second completes. Every figure sits in
// a fixed-width field, so the pane keeps one width. Allocation-free once on: the text is formatted
// into a buffer this pane owns and handed to the label as a span, since a pane that churned the GC
// would distort the counts it shows. Each figure goes through its own type's TryFormat rather than
// an interpolated handler, whose generic AppendFormatted boxes a double until the JIT has tiered it.
internal sealed class FramePane : ScreenEntity
{
    private const int Padding = 4;
    private const double SecondMs = 1000.0;

    private static readonly BitmapFont Font = BitmapFont.Default;
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private readonly ColorRect _backdrop;
    private readonly Label _label;

    // Sized past the widest layout the fields allow; a figure too wide for its field takes what it
    // needs and the pane grows for that second, and one that would overflow the buffer is cut.
    private readonly char[] _text = new char[256];
    private int _length;

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

    // Drops the second in progress and shows zeros until a whole second has been pushed, so a
    // pane switched back on never joins samples from before it was off.
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

    // Rewrites the label from one second's figures; the collection counts and the heap are read
    // here, at the second's end.
    private void Publish(double frameMs, double worstMs, double updateMs, double drawMs, double stepsPerSecond)
    {
        double fps = frameMs > 0 ? SecondMs / frameMs : 0;
        double heapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        Figures = new FrameFigures(fps, frameMs, worstMs, updateMs, drawMs, stepsPerSecond);

        _length = 0;
        Write("fps ");
        Write(fps, 6, "F1");
        Write("   frame ");
        Write(frameMs, 6, "F2");
        Write(" ms  max ");
        Write(worstMs, 6, "F2");
        Write("\nupdate ");
        Write(updateMs, 6, "F2");
        Write(" ms   draw ");
        Write(drawMs, 6, "F2");
        Write(" ms\nsteps ");
        Write(stepsPerSecond, 6, "F1");
        Write("/s   gc ");
        Write(GC.CollectionCount(0), 5);
        Write("/");
        Write(GC.CollectionCount(1), 3);
        Write("/");
        Write(GC.CollectionCount(2), 2);
        Write("   heap ");
        Write(heapMb, 7, "F2");
        Write(" MB");

        ReadOnlySpan<char> text = _text.AsSpan(0, _length);
        _label.SetText(text);

        Vector2 measured = Font.Measure(text);
        float width = (int)measured.X + (Padding * 2);
        _backdrop.Offset = new Vector2(-width, 0f);
        _backdrop.Size = new Vector2(width, measured.Y + (Padding * 2));
        _label.Offset = new Vector2(Padding - width, Padding);
    }

    private void Write(ReadOnlySpan<char> text)
    {
        Span<char> free = _text.AsSpan(_length);
        if (text.Length > free.Length)
        {
            text = text[..free.Length];
        }

        text.CopyTo(free);
        _length += text.Length;
    }

    // Right-aligned in width columns.
    private void Write(double value, int width, string format)
    {
        Span<char> digits = stackalloc char[32];
        value.TryFormat(digits, out int written, format, Culture);
        Write(digits[..written], width);
    }

    private void Write(int value, int width)
    {
        Span<char> digits = stackalloc char[16];
        value.TryFormat(digits, out int written, default, Culture);
        Write(digits[..written], width);
    }

    private void Write(ReadOnlySpan<char> figure, int width)
    {
        for (int pad = figure.Length; pad < width; pad++)
        {
            Write(" ");
        }

        Write(figure);
    }
}
