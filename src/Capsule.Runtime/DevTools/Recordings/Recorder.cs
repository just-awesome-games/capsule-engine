using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Recordings;

// Records a windowed run from its first step: each step's snapshot, the output extent and refresh rate
// as they change, the overlay's host acts, and a checksum once a simulated second. A step writes a few
// bytes into a buffer allocated up front.
internal sealed class Recorder
{
    // About a megabyte, hours of keyboard play, and the size of each later chunk.
    internal const int ChunkBytes = 1 << 20;

    private readonly RecordingHeader _header;
    private readonly ChunkedBuffer _body = new(ChunkBytes);
    private readonly long _checkpointEvery;

    // Null until the host it records is built. A run that crashed while its host was built leaves a
    // recording of its boot state alone.
    private SceneHost? _scenes;

    private DeviceSnapshot _previous;
    private long _repeats;

    // NaN until the first step writes the run's.
    private Vector2 _output = new(float.NaN);
    private double _refreshRate = double.NaN;

    // Steps recorded so far.
    internal long Ticks { get; private set; }

    // The header's saves, which the run's medium fills as it restores them.
    internal List<RecordedSave> Restored { get; } = [];

    // Built before the game's startup runs, from the boot state and shell settings it cannot yet have
    // changed.
    internal Recorder(RecordingHeader header)
    {
        _header = header;
        header.Saves = Restored;
        _checkpointEvery = Math.Max(1, (long)Math.Round(1 / header.StepSeconds));
    }

    // The host whose steps and state are recorded, once it is built.
    internal void Attach(SceneHost scenes) => _scenes = scenes;

    // An act lands inside the step that follows it.
    internal void Act(in HostAct act)
    {
        Flush();
        _body.WriteByte(RecordingFormat.Act);
        RecordingBody.WriteAct(_body, in act);
    }

    // The step about to run on snapshot, before the input state advances to it. The first step takes the
    // run's initial device and key layout.
    internal void Step(in DeviceSnapshot snapshot, Vector2 output, InputState input)
    {
        if (Ticks == 0)
        {
            _header.Device = input.ActiveDevice;
            _header.KeyLayout = input.KeyLayout is { } layout ? [.. layout] : null;
        }

        if (!RecordingBody.SameBits(output, _output))
        {
            Flush();
            _output = output;
            _body.WriteByte(RecordingFormat.Output);
            _body.WriteVector(output);
        }

        double rate = _scenes!.Run.Display.RefreshRate;
        if (BitConverter.DoubleToUInt64Bits(rate) != BitConverter.DoubleToUInt64Bits(_refreshRate))
        {
            Flush();
            _refreshRate = rate;
            _body.WriteByte(RecordingFormat.RefreshRate);
            _body.WriteDouble(rate);
        }

        Ticks++;
        int changes = RecordingBody.ChangeMask(in snapshot, in _previous);
        if (Ticks > 1 && changes == 0)
        {
            _repeats++;

            return;
        }

        Flush();
        _body.WriteByte(RecordingFormat.Step);
        RecordingBody.WriteSnapshot(_body, changes, in snapshot, in _previous);
        _previous = snapshot;
    }

    // After the step that brought the run to tick.
    internal void Stepped(long tick)
    {
        if (tick % _checkpointEvery != 0)
        {
            return;
        }

        Flush();
        _body.WriteByte(RecordingFormat.Checkpoint);
        _body.WriteVarint((ulong)tick);
        _body.WriteUInt64(Checksum(tick, _scenes!));
    }

    // The run so far as a file. A crashed run's last step never finished, and its end carries no
    // checksum. The live buffer is left as it was, and recording carries on.
    internal void Write(Stream destination, bool crashed)
    {
        ChunkedBuffer head = new(4096);
        foreach (byte unit in RecordingFormat.Magic)
        {
            head.WriteByte(unit);
        }

        head.WriteVarint(RecordingFormat.Version);
        _header.Write(head);
        head.CopyTo(destination);
        _body.CopyTo(destination);

        ChunkedBuffer tail = new(64);
        if (_repeats > 0)
        {
            tail.WriteByte(RecordingFormat.Repeat);
            tail.WriteVarint((ulong)_repeats);
        }

        tail.WriteByte(RecordingFormat.End);
        tail.WriteVarint((ulong)Ticks);
        ulong? checksum = crashed || _scenes is null ? null : Checksum(Ticks, _scenes);
        tail.WriteByte((byte)((checksum is null ? 0 : 1) | (_scenes is { ExitRequested: true } ? 2 : 0)));
        tail.WriteUInt64(checksum ?? 0);
        tail.CopyTo(destination);
    }

    // The state a replay compares at tick. An exited run's scene is torn down, and only the run's own
    // state is left to compare.
    internal static ulong Checksum(long tick, SceneHost scenes) =>
        SimulationChecksum.Compute(tick, scenes.Scene, scenes.Run.Random, entities: !scenes.ExitRequested);

    // Writes the steps a Repeat stands for before anything else lands.
    private void Flush()
    {
        if (_repeats == 0)
        {
            return;
        }

        _body.WriteByte(RecordingFormat.Repeat);
        _body.WriteVarint((ulong)_repeats);
        _repeats = 0;
    }
}
