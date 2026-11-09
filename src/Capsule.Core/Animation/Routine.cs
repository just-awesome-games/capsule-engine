using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Capsule.Animation;

/// <summary>
/// A call of an <see langword="async"/> method that a <see cref="Sequence"/> steps, awaiting <see cref="Wait"/> values
/// and other routines.
/// </summary>
/// <remarks>
/// Calling the method runs nothing. The routine starts when a sequence starts it or another routine awaits it, and it
/// runs once. Calls are pooled per method, and an optimized build allocates one only when more of a method's calls are
/// live than ever before. The default value is empty.
/// </remarks>
/// <example>
/// <code>
/// private async Routine Patrol()
/// {
///     while (true)
///     {
///         await Walk(north);
///         await Wait.Ticks(30);
///         await Walk(south);
///     }
/// }
/// </code>
/// </example>
[AsyncMethodBuilder(typeof(Builder))]
public readonly partial struct Routine
{
    internal RoutineCall? Call { get; }

    internal int Generation { get; }

    internal Routine(RoutineCall call)
    {
        Call = call;
        Generation = call.Generation;
    }

    /// <summary>Lets an <see langword="async"/> routine await this one. Game code awaits the routine instead.</summary>
    /// <exception cref="InvalidOperationException">The routine has already run or is running.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Awaiter GetAwaiter() => new(Claim());

    internal RoutineCall? Claim()
    {
        if (Call is not { } call)
        {
            return null;
        }

        if (!call.TryClaim(Generation))
        {
            throw new InvalidOperationException(
                "This routine has already run or is running. Call its method again for a new run.");
        }

        return call;
    }
}
