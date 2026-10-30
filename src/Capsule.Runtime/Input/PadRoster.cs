using Capsule.Input;

namespace Capsule.Runtime.Input;

// An open pad: SDL's joystick id and gamepad handle, the name the log uses, its button family and
// the PadButton bits its mapping lacks.
internal readonly record struct Pad(uint Id, nint Handle, string Name, PadFamily Family, uint Lacking = 0);

// The open pads in connection order and the active one, the pad last used. SDL numbers joystick
// instances in increasing order as it detects them and lists them per driver. Connection order is id
// order. The active pad is the earliest connected until another is used, and the earliest left
// when it goes.
internal sealed class PadRoster
{
    private readonly List<Pad> _pads = new(InputEvents.MaxPressingPads);
    private uint _active;

    // The active pad's SDL joystick id, or 0 with none open.
    internal uint ActiveId => _active;

    internal bool TryGetActive(out Pad pad)
    {
        int index = IndexOf(_active);
        pad = index >= 0 ? _pads[index] : default;

        return index >= 0;
    }

    // Makes pad active when it is open. Any other id, 0 among them, changes nothing.
    internal void Use(uint pad)
    {
        if (IndexOf(pad) >= 0)
        {
            _active = pad;
        }
    }

    // Closes the open pads connected leaves out and opens the ones it adds, whatever order connected
    // lists them in. open returns null for a pad that could not be opened.
    internal void Sync(ReadOnlySpan<uint> connected, Func<uint, Pad?> open, Action<Pad> close)
    {
        for (int index = _pads.Count - 1; index >= 0; index--)
        {
            if (!connected.Contains(_pads[index].Id))
            {
                close(_pads[index]);
                _pads.RemoveAt(index);
            }
        }

        Span<uint> found = connected.Length <= 64 ? stackalloc uint[connected.Length] : new uint[connected.Length];
        int count = 0;
        foreach (uint id in connected)
        {
            if (id != 0 && IndexOf(id) < 0)
            {
                found[count++] = id;
            }
        }

        found = found[..count];

        // A sort given no comparison allocates on every call in a NativeAOT build.
        found.Sort(static (left, right) => left.CompareTo(right));
        foreach (uint id in found)
        {
            if (open(id) is { } pad)
            {
                int at = _pads.FindIndex(existing => existing.Id > id);
                _pads.Insert(at < 0 ? _pads.Count : at, pad);
            }
        }

        if (IndexOf(_active) < 0)
        {
            _active = _pads.Count > 0 ? _pads[0].Id : 0;
        }
    }

    // Replaces every open pad with what refresh reads of it.
    internal void Refresh(Func<Pad, Pad> refresh)
    {
        for (int index = 0; index < _pads.Count; index++)
        {
            _pads[index] = refresh(_pads[index]);
        }
    }

    // Closes every open pad.
    internal void Clear(Action<Pad> close)
    {
        foreach (Pad pad in _pads)
        {
            close(pad);
        }

        _pads.Clear();
        _active = 0;
    }

    private int IndexOf(uint id)
    {
        for (int index = 0; index < _pads.Count; index++)
        {
            if (id != 0 && _pads[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
