using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Input;

public sealed class PadRosterTests
{
    // SDL lists pads per driver, and a batch found at once can list a later pad first. Ids count up as
    // SDL detects pads, and the earliest connected is the lowest and active first. When the active pad goes,
    // a pad left open takes over only once it is used.
    [Fact]
    public void PadsTakeConnectionOrderFromTheirIds_WhateverOrderSdlListsThem()
    {
        PadRoster pads = new();
        List<uint> opened = [];

        pads.Sync([7, 3, 5], id => Open(id, opened), static _ => { });
        Assert.Equal([3u, 5u, 7u], opened);
        Assert.Equal(3u, pads.ActiveId);

        pads.Use(7);
        pads.Sync([5, 3], id => Open(id, opened), static _ => { });
        Assert.Equal(0u, pads.ActiveId);

        pads.Use(5);
        Assert.Equal(5u, pads.ActiveId);
    }

    // An idle or virtual pad left open has a lower id than the pad plugged back in, and never takes over from it.
    [Fact]
    public void APadConnectingWhileNoneIsActive_BecomesActive_OverAnIdlePadLeftOpen()
    {
        PadRoster pads = new();
        List<uint> opened = [];

        pads.Sync([2, 4], id => Open(id, opened), static _ => { });
        pads.Use(4);
        pads.Sync([2], id => Open(id, opened), static _ => { });
        Assert.Equal(0u, pads.ActiveId);

        pads.Sync([2, 9], id => Open(id, opened), static _ => { });
        Assert.Equal(9u, pads.ActiveId);

        // A pad connecting while one is active leaves it active.
        pads.Sync([2, 9, 11], id => Open(id, opened), static _ => { });
        Assert.Equal(9u, pads.ActiveId);

        // A pad found in the same sync that loses the active one may have connected before it went.
        pads.Sync([2, 11, 12], id => Open(id, opened), static _ => { });
        Assert.Equal(0u, pads.ActiveId);
    }

    private static Pad? Open(uint id, List<uint> opened)
    {
        opened.Add(id);

        return new Pad(id, nint.Zero, "pad", PadFamily.Generic);
    }
}
