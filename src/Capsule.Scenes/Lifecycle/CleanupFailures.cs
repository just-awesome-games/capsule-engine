using System.Runtime.ExceptionServices;

namespace Capsule.Scenes.Lifecycle;

internal static class CleanupFailures
{
    internal static void Throw(List<Exception>? failures)
    {
        if (failures is [Exception failure])
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more scene cleanup hooks failed.", failures);
        }
    }
}
