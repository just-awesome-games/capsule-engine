namespace Capsule.Runtime.Gpu;

// When the staging buffer returns to its steady size after a burst of uploads, such as a scene's
// textures. A burst grows the buffer or overflows it into further copies, which SDL keeps for the
// buffer's life. Once every frame for the quiet period has staged no more than the steady size, the
// buffer is recreated at that size and the copies are released.
internal struct StagingSettle(long quietTicks)
{
    // Room for a quiet frame's sprites and uniforms. Above 2 MB, SDL's Vulkan driver gives each
    // transfer buffer a 64 MB block of its own.
    internal const int SteadyBytes = 1 << 20;

    private long _quietSince;
    private bool _burst;

    // The rows of rowBytes each that one staged band of a texture holds: as many as fit SteadyBytes,
    // and one when a single row does not fit.
    internal static int BandRows(int rowBytes) => Math.Max(1, SteadyBytes / rowBytes);

    // Records one submitted frame: the bytes it staged, whether it grew or overflowed the buffer, and
    // the timestamp it ended at. True when the buffer is to be recreated at SteadyBytes now. A frame
    // that overflowed is part of the burst, however few bytes alignment let it stage.
    internal bool EndFrame(int staged, bool overflowed, long now)
    {
        _burst |= overflowed;
        if (overflowed || !_burst || staged > SteadyBytes)
        {
            _quietSince = now;
            return false;
        }

        if (now - _quietSince < quietTicks)
        {
            return false;
        }

        _burst = false;
        _quietSince = now;
        return true;
    }
}
