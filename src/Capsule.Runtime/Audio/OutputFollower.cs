namespace Capsule.Runtime.Audio;

// Keeps sound on the system's default output. The device announces a change from a thread of its
// own, where nothing may be reopened, so the change is only noted there and acted on from the game
// thread's next update. A reopen that fails — a headset still negotiating, or an output gone before
// its replacement was announced — is retried on an interval rather than given up on, and a device
// the system has disconnected is reopened even when no announcement arrived, noticed on that same
// interval rather than by asking the library every frame.
internal sealed class OutputFollower(Func<bool> reopen, Func<bool> connected)
{
    internal const double RetrySeconds = 0.5;

    private volatile bool _announced;
    private bool _pending;
    private double _untilRetry;
    private double _untilPoll;

    // Callable from any thread.
    internal void DefaultChanged() => _announced = true;

    // Once a frame, on the game thread.
    internal void Update(double elapsedSeconds)
    {
        if (_announced)
        {
            _announced = false;
            _pending = true;
            _untilRetry = 0.0;
        }
        else if (!_pending)
        {
            // On the retry interval rather than every frame: asking the library whether the device
            // is still there is a driver call, and a disconnection half a second late is inaudible.
            _untilPoll -= elapsedSeconds;
            if (_untilPoll <= 0.0)
            {
                _untilPoll = RetrySeconds;
                if (!connected())
                {
                    _pending = true;
                    _untilRetry = 0.0;
                }
            }
        }

        if (!_pending)
        {
            return;
        }

        _untilRetry -= elapsedSeconds;
        if (_untilRetry > 0.0)
        {
            return;
        }

        if (reopen())
        {
            _pending = false;
        }
        else
        {
            _untilRetry = RetrySeconds;
        }
    }
}
