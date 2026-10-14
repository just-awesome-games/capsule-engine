namespace Capsule.Animation;

/// <summary>
/// Runs scripted beats written as an iterator of <see cref="Wait"/> values. Its owner holds it, steps it
/// each step, and the beats run in that step's order.
/// </summary>
/// <remarks>
/// A sequence holds whenever its owner does, through a pause or a freeze. Beats that pause the scene
/// belong to the scene or to an entity whose step mode keeps it stepping. A sequence whose owner leaves
/// the scene simply stops being stepped, and an owner that must release something calls
/// <see cref="Stop"/>. Calling the iterator method allocates it once, and stepping allocates nothing.
/// <para>
/// The beats include their conditions and the <c>finally</c> blocks a stop runs. An exception they
/// throw leaves the sequence stopped and propagates from the call that ran them.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// private readonly Sequence _showLift = new();
///
/// protected override void OnStep(in StepContext context) => _showLift.Step();
///
/// private IEnumerable&lt;Wait&gt; ShowLift(Player player)
/// {
///     player.HasControl = false;
///     Scene.Camera.Follow(_lift);
///     yield return Wait.Ticks(40);
///     _lift.Running = true;
///     yield return Wait.Until(() =&gt; _lift.HasRisen);
///     Scene.Camera.Follow(player);
///     player.HasControl = true;
/// }
/// </code>
/// </example>
public sealed class Sequence
{
    private IEnumerator<Wait>? _beats;
    private int _ticksLeft;
    private Func<bool>? _until;

    // Set while the beats run, which may not start, stop or step the sequence running them.
    private bool _inBeats;

    /// <summary>Whether beats remain to run. False before the first start and once the iterator returns or is stopped.</summary>
    public bool IsRunning => _beats is not null;

    /// <summary>
    /// Stops any running beats, then runs <paramref name="beats"/> up to their first wait before returning.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from this sequence's own beats.</exception>
    public void Start(IEnumerable<Wait> beats)
    {
        ArgumentNullException.ThrowIfNull(beats);
        Stop();
        _beats = beats.GetEnumerator();
        RunBeats();
    }

    /// <summary>
    /// Spends one tick of the current wait, or checks its condition, and runs the beats on to their next
    /// wait once it is over. On a sequence that is not running it does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from this sequence's own beats.</exception>
    public void Step()
    {
        ThrowIfInBeats();
        if (_beats is null)
        {
            return;
        }

        if (_until is { } condition ? Check(condition) : --_ticksLeft <= 0)
        {
            RunBeats();
        }
    }

    /// <summary>Ends the beats where they stand and runs the <c>finally</c> blocks they are inside.</summary>
    /// <exception cref="InvalidOperationException">Called from this sequence's own beats.</exception>
    public void Stop()
    {
        ThrowIfInBeats();
        _inBeats = true;
        try
        {
            End();
        }
        finally
        {
            _inBeats = false;
        }
    }

    private void RunBeats()
    {
        _inBeats = true;
        try
        {
            while (_beats!.MoveNext())
            {
                Wait wait = _beats.Current;
                if (wait.Condition is { } condition)
                {
                    if (!condition())
                    {
                        _until = condition;
                        return;
                    }
                }
                else if (wait.TickCount > 0)
                {
                    _until = null;
                    _ticksLeft = wait.TickCount;
                    return;
                }
            }

            End();
        }
        catch
        {
            End();
            throw;
        }
        finally
        {
            _inBeats = false;
        }
    }

    private bool Check(Func<bool> condition)
    {
        _inBeats = true;
        try
        {
            return condition();
        }
        catch
        {
            End();
            throw;
        }
        finally
        {
            _inBeats = false;
        }
    }

    // Disposing runs the finally blocks the beats stand in, which count as the beats' own code.
    private void End()
    {
        IEnumerator<Wait>? beats = _beats;
        _beats = null;
        _until = null;
        _ticksLeft = 0;
        beats?.Dispose();
    }

    private void ThrowIfInBeats()
    {
        if (_inBeats)
        {
            throw new InvalidOperationException(
                "A sequence cannot start, stop or step itself from its own beats. Return from the iterator, or use yield break, to end it.");
        }
    }
}
