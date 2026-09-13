using System.Globalization;
using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Runtime.Diagnostics;

// The development overlay is an ordinary screen-space scene. Its run is owned by DebugOverlay and
// carries point sampling because the host presents this scene at an integer scale.
internal sealed class DebugScene : Scene
{
    private const int Padding = 4;

    private readonly ColorRect _backdrop;
    private readonly Label _label;

    private bool _readoutInitialized;
    private string _readout = string.Empty;
    private string _readoutScene = string.Empty;
    private long _readoutTick;
    private int _readoutSteps;

    internal DebugScene(string sceneName, long tick, int steps)
    {
        Sampling = TextureSampling.Point;

        ScreenEntity overlay = new(Anchor.TopLeft, Vector2.Zero);
        _backdrop = new ColorRect(Vector2.Zero)
        {
            Color = new ColorRgba(0, 0, 0, 160),
        };
        _label = new Label(BitmapFont.Default)
        {
            Offset = new Vector2(Padding, Padding),
        };

        overlay.Add(_backdrop);
        overlay.Add(_label);
        Add(overlay);

        SetReadout(sceneName, tick, steps);
    }

    internal string Readout => _readout;

    internal Label Label => _label;

    internal ColorRect Backdrop => _backdrop;

    internal void SetReadout(string sceneName, long tick, int steps)
    {
        ArgumentNullException.ThrowIfNull(sceneName);

        if (_readoutInitialized
            && string.Equals(_readoutScene, sceneName, StringComparison.Ordinal)
            && _readoutTick == tick
            && _readoutSteps == steps)
        {
            return;
        }

        _readout = string.Create(
            CultureInfo.InvariantCulture,
            $"{sceneName}  tick {tick}  steps {steps}");
        _readoutScene = sceneName;
        _readoutTick = tick;
        _readoutSteps = steps;
        _readoutInitialized = true;

        _label.Text = _readout;
        int textWidth = (int)BitmapFont.Default.Measure(_readout).X;
        _backdrop.Size = new Vector2(
            textWidth + (Padding * 2),
            BitmapFont.Default.LineHeight + (Padding * 2));
    }
}
