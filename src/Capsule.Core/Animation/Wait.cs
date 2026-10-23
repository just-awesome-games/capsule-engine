namespace Capsule.Animation;

/// <summary>
/// What a <see cref="Sequence"/> waits for before its next beat, yielded from the iterator it runs.
/// </summary>
/// <remarks>
/// The default value continues at once.
/// </remarks>
public readonly struct Wait
{
    internal int TickCount { get; }

    internal Func<bool>? Condition { get; }

    private Wait(int ticks, Func<bool>? condition)
    {
        TickCount = ticks;
        Condition = condition;
    }

    /// <summary>
    /// Resumes on the <paramref name="ticks"/>th <see cref="Sequence.Step"/> after the yield. Zero continues at once.
    /// </summary>
    /// <param name="ticks">Fixed steps to wait. Must not be negative.</param>
    public static Wait Ticks(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        return new Wait(ticks, null);
    }

    /// <summary>
    /// Resumes once <paramref name="condition"/> reads true, checked at the yield and then on each <see cref="Sequence.Step"/>.
    /// </summary>
    /// <remarks>
    /// A condition already true at the yield continues at once. A lambda capturing state, or a method group naming an
    /// instance method, allocates a delegate each time its yield runs. Nothing allocates while it is checked.
    /// </remarks>
    public static Wait Until(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return new Wait(0, condition);
    }
}
