using System.Numerics;

namespace Capsule.Runtime.DevTools.Overlay;

// The overlay view's smooth pan and zoom between two framings. The closed-form path keeps the motion on
// screen at an even pace. A far target zooms out part-way to show both ends, and a near one is almost a
// straight ease. It is a plain value, and stepping it allocates nothing.
internal struct Flight
{
    // A centre closer than this share of the wider end's span counts as the same centre. The flight is then a
    // pure zoom.
    private const double PureZoomShare = 1e-6;

    private Vector2 _from;
    private Vector2 _to;

    // Both ends' visible widths in world units, and the widest the path may reach.
    private double _widthFrom;
    private double _widthTo;
    private double _widthMax;

    // The path's start parameter, its length, and the centres' distance. A pure zoom has no distance.
    private double _start;
    private double _length;
    private double _distance;
    private bool _pureZoom;

    private double _elapsed;
    private double _duration;

    // Whether a flight is under way. A finished or cancelled one is the default value.
    internal bool Active { get; private set; }

    // The visible width, in world units, of a view at the game camera's size. A path's widths scale with
    // the view's size by this.
    internal float UnitWidth { get; private set; }

    // Starts a flight from a view centred at from with the visible width widthFrom to one at to with
    // widthTo, where unitWidth is the width at the game camera's size. The path stays within widthMax.
    // Two framings that agree give an inactive flight.
    internal static Flight Begin(Vector2 from, double widthFrom, Vector2 to, double widthTo, float unitWidth, double widthMax)
    {
        double distance = Vector2.Distance(from, to);
        if (!(widthFrom > 0.0) || !(widthTo > 0.0) || !double.IsFinite(distance))
        {
            return default;
        }

        Flight flight = new()
        {
            _from = from,
            _to = to,
            _widthFrom = widthFrom,
            _widthTo = widthTo,
            _widthMax = widthMax,
            _distance = distance,
            UnitWidth = unitWidth,
        };

        double rho = FreeCamera.FlightRho;
        if (distance <= Math.Max(widthFrom, widthTo) * PureZoomShare)
        {
            flight._pureZoom = true;
            flight._length = Math.Abs(Math.Log(widthTo / widthFrom)) / rho;
        }
        else
        {
            double rho2 = rho * rho;
            double rho4 = rho2 * rho2;
            double spread = (widthTo * widthTo) - (widthFrom * widthFrom);
            double b0 = (spread + (rho4 * distance * distance)) / (2.0 * widthFrom * rho2 * distance);
            double b1 = (spread - (rho4 * distance * distance)) / (2.0 * widthTo * rho2 * distance);

            // Uses asinh, which is ln(sqrt(b^2 + 1) + b) and keeps its precision at the large b of a short hop.
            flight._start = -Math.Asinh(b0);
            flight._length = (-Math.Asinh(b1) - flight._start) / rho;
        }

        if (!(flight._length > 0.0) || !double.IsFinite(flight._length))
        {
            return default;
        }

        flight._duration = Math.Clamp(
            flight._length * FreeCamera.FlightSecondsPerLength,
            FreeCamera.FlightMinSeconds,
            FreeCamera.FlightMaxSeconds);
        flight.Active = true;

        return flight;
    }

    // Moves on by seconds and returns where the view stands. The result is the target exactly once the flight
    // ends. The time fraction is eased in and out.
    internal (Vector2 Center, float Width) Step(double seconds)
    {
        _elapsed += seconds;
        double time = Math.Min(1.0, _elapsed / _duration);
        if (time >= 1.0)
        {
            Active = false;

            return (_to, (float)_widthTo);
        }

        double parameter = time * time * (3.0 - (2.0 * time)) * _length;
        double rho = FreeCamera.FlightRho;
        if (_pureZoom)
        {
            double sign = _widthTo > _widthFrom ? 1.0 : -1.0;

            return (_to, (float)Math.Min(_widthFrom * Math.Exp(sign * rho * parameter), _widthMax));
        }

        double angle = (rho * parameter) + _start;
        double travelled = _widthFrom / (rho * rho) * ((Math.Cosh(_start) * Math.Tanh(angle)) - Math.Sinh(_start));
        double width = _widthFrom * Math.Cosh(_start) / Math.Cosh(angle);

        return (Vector2.Lerp(_from, _to, (float)(travelled / _distance)), (float)Math.Min(width, _widthMax));
    }
}
