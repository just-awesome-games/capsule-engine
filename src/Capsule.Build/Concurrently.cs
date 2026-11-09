using System.Runtime.ExceptionServices;

namespace Capsule.Build;

internal static class Concurrently
{
    /// <summary>Runs <paramref name="body"/> once for each index below <paramref name="count"/>, at most <paramref name="degree"/> at once.</summary>
    /// <remarks>
    /// A degree of 1 runs every index in order on the calling thread and stops at the first exception,
    /// which is rethrown as thrown. Otherwise every index runs. One failure is rethrown as thrown, and
    /// several as one <see cref="AggregateException"/> holding each in index order.
    /// </remarks>
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

        Exception?[] failures = new Exception?[count];
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = degree }, i =>
        {
            try
            {
                body(i);
            }
            catch (Exception ex)
            {
                failures[i] = ex;
            }
        });

        Exception[] failed = [.. failures.OfType<Exception>()];
        if (failed.Length == 1)
        {
            ExceptionDispatchInfo.Throw(failed[0]);
        }

        if (failed.Length > 1)
        {
            throw new AggregateException(failed);
        }
    }
}
