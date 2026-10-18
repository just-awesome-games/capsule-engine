namespace Capsule;

// The cleanup a failed step runs before its failure propagates.
internal static class Failure
{
    // Runs cleanup after failure. When cleanup throws too, both leave as one AggregateException carrying
    // message. When it succeeds, this returns and the caller rethrows failure with its stack intact.
    internal static void CleanUp(Exception failure, Action cleanup, string message)
    {
        try
        {
            cleanup();
        }
        catch (Exception cleanupFailure)
        {
            throw new AggregateException(message, failure, cleanupFailure);
        }
    }
}
