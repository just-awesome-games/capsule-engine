using System.Runtime.ExceptionServices;

namespace Capsule.Build;

internal static class Concurrently
{
    /// <summary>Runs <paramref name="body"/> once for each index below <paramref name="count"/>, at most <paramref name="degree"/> at once.</summary>
    /// <remarks>A degree of 1 runs every index in order on the calling thread. An exception is rethrown as thrown, not wrapped.</remarks>
    internal static void For(int count, int degree, Action<int> body)
    {
        if (degree <= 1 || count <= 1)
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }

            return;
        }

        try
        {
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = degree }, body);
        }
        catch (AggregateException ex)
        {
            ExceptionDispatchInfo.Throw(ex.InnerExceptions[0]);
        }
    }
}
