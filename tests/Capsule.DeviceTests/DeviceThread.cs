using System.Collections.Concurrent;

namespace Capsule.DeviceTests;

// MonoGame binds its device to the first thread that touches it, and xunit moves specs between threads.
internal static class DeviceThread
{
    private static readonly BlockingCollection<Action> Work = [];

    static DeviceThread()
    {
        Thread thread = new(static () =>
        {
            foreach (Action work in Work.GetConsumingEnumerable())
            {
                work();
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
    }

    internal static void Run(Action spec)
    {
        Exception? failure = null;
        using ManualResetEventSlim done = new();
        Work.Add(() =>
        {
            try
            {
                spec();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        }
    }
}

// A spec that opens a window, skipped on a Linux machine with no display, which is CI's.
internal sealed class DeviceFactAttribute : FactAttribute
{
    public DeviceFactAttribute()
    {
        if (OperatingSystem.IsLinux()
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Skip = "No display to open a window on.";
        }
    }
}
