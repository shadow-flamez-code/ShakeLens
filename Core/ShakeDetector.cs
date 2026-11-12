// ============================================================================
//  ShakeLens - the shake detector
//
//  The gesture is deliberately hard to trigger: a shake is a run of quick
//  direction reversals with a large amplitude, inside a short window and at
//  a high speed.  Ordinary mouse movement never comes close.
// ============================================================================

namespace ShakeLens.Core;

public sealed class ShakeDetector
{
    public enum Sensitivity
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    private readonly record struct Tuning(
        int MinReversals,
        double WindowSeconds,
        double Amplitude,
        double MinSpeed);

    // A comfortable shake is 3-5 full cycles per second, so a direction
    // change happens 6-10 times per second.  The window therefore has to be
    // generous, otherwise no human can ever reach the required number of
    // reversals.
    private static Tuning TuningFor(Sensitivity sensitivity) => sensitivity switch
    {
        // strict: 5 direction changes inside 1.1 seconds
        Sensitivity.Low => new Tuning(5, 1.10, 35, 420),
        // balanced (default): 4 direction changes inside 1 second
        Sensitivity.Medium => new Tuning(4, 1.00, 26, 320),
        // light: 3 direction changes inside 1 second
        _ => new Tuning(3, 1.00, 18, 250)
    };

    private Tuning _tuning = TuningFor(Sensitivity.Medium);
    private readonly List<double> _reversals = new();

    private bool _started;
    private double _lastX, _lastY;
    private double _dirX, _dirY;
    private double _anchorX, _anchorY;
    private double _travel;

    public ShakeDetector(Sensitivity sensitivity = Sensitivity.Medium) => Configure(sensitivity);

    public void Configure(Sensitivity sensitivity)
    {
        _tuning = TuningFor(sensitivity);
        Reset();
    }

    public void Reset()
    {
        _started = false;
        _dirX = _dirY = 0;
        _anchorX = _anchorY = 0;
        _travel = 0;
        _reversals.Clear();
    }

    /// <summary>Feeds one cursor sample, returns true when a shake was recognised.</summary>
    public bool Push(double time, double x, double y)
    {
        if (!_started)
        {
            _started = true;
            _lastX = x;
            _lastY = y;
            return false;
        }
        double dx = x - _lastX;
        double dy = y - _lastY;
        _lastX = x;
        _lastY = y;

        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 1.0) return false;          // ignore jitter
        Samples++;

        // drop reversals that fell out of the window
        while (_reversals.Count > 0 && time - _reversals[0] > _tuning.WindowSeconds)
            _reversals.RemoveAt(0);

        int before = _reversals.Count;
        AxisStep(true, x, time);
        AxisStep(false, y, time);

        // the speed is measured from the first reversal onwards
        if (_reversals.Count > before && before == 0) _travel = distance;
        else _travel += distance;

        if (_reversals.Count < _tuning.MinReversals) return false;

        double span = time - _reversals[0];
        if (span <= 0) return false;
        if (_travel / span < _tuning.MinSpeed) return false;

        Reset();
        return true;
    }

    /// <summary>Diagnostics: how many reversals the detector has seen.</summary>
    public int ReversalCount => _reversals.Count;

    /// <summary>Diagnostics: how far the pointer travelled in the current window.</summary>
    public double Travel => _travel;

    /// <summary>Diagnostics: how many samples the detector has seen.</summary>
    public int Samples { get; private set; }

    private void AxisStep(bool horizontal, double value, double time)
    {
        ref double direction = ref (horizontal ? ref _dirX : ref _dirY);
        ref double anchor = ref (horizontal ? ref _anchorX : ref _anchorY);

        double delta = value - anchor;
        if (Math.Abs(delta) < _tuning.Amplitude) return;

        double sign = delta > 0 ? 1 : -1;
        if (direction != 0 && sign != direction) _reversals.Add(time);
        direction = sign;
        anchor = value;
    }
}
