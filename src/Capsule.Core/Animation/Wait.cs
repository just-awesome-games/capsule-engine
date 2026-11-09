using System.ComponentModel;

namespace Capsule.Animation;

/// <summary>
/// What a <see cref="Routine"/> awaits before it goes on.
/// </summary>
/// <remarks>
/// The default value continues at once.
/// </remarks>
public readonly partial struct Wait
{
    private static readonly Func<object?, Delegate, bool> Stateless = static (_, condition) => ((Func<bool>)condition)();

    private readonly object? _state;

    private readonly Delegate? _condition;

    // Calls the condition with its state. Each state type shares one, and a wait allocates nothing.
    private readonly Func<object?, Delegate, bool>? _check;

    internal int TickCount { get; }

    internal bool HasCondition => _check is not null;

    private Wait(int ticks) => TickCount = ticks;

    private Wait(object? state, Delegate condition, Func<object?, Delegate, bool> check)
    {
        _state = state;
        _condition = condition;
        _check = check;
    }

    /// <summary>
    /// Resumes on the <paramref name="ticks"/>th <see cref="Sequence.Step"/> after the await. Zero continues at once.
    /// </summary>
    /// <param name="ticks">Fixed steps to wait. Must not be negative.</param>
    public static Wait Ticks(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        return new Wait(ticks);
    }

    /// <summary>
    /// Resumes once <paramref name="condition"/> reads true, checked at the await and then on each <see cref="Sequence.Step"/>.
    /// </summary>
    /// <remarks>
    /// A condition already true at the await continues at once. A lambda that captures anything allocates each time its
    /// await runs. A routine that runs often passes its state to the other overload instead.
    /// </remarks>
    public static Wait Until(Func<bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return new Wait(null, condition, Stateless);
    }

    /// <summary>
    /// Resumes once <paramref name="condition"/> reads true of <paramref name="state"/>, checked at the await and then
    /// on each <see cref="Sequence.Step"/>.
    /// </summary>
    /// <remarks>
    /// A condition already true at the await continues at once. Mark the lambda <see langword="static"/>. It then reads
    /// only the state it is passed, and the wait allocates nothing.
    /// </remarks>
    /// <example>
    /// <code>
    /// await Wait.Until(_animator, static animator =&gt; animator.IsFinished);
    /// await Wait.Until(this, static avatar =&gt; avatar.IsOnFloor);
    /// </code>
    /// </example>
    /// <typeparam name="TState">The type of the state the condition reads. A class, which the wait holds unboxed.</typeparam>
    public static Wait Until<TState>(TState state, Func<TState, bool> condition)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(condition);

        return new Wait(state, condition, StateCheck<TState>.Check);
    }

    /// <summary>Lets a <see cref="Routine"/> await this wait. Game code awaits the value instead.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Awaiter GetAwaiter() => new(this);

    internal bool IsMet() => _check!(_state, _condition!);

    private static class StateCheck<TState>
        where TState : class
    {
        internal static readonly Func<object?, Delegate, bool> Check =
            static (state, condition) => ((Func<TState, bool>)condition)((TState)state!);
    }
}
