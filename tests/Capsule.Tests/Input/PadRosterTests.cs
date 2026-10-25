using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Input;

public sealed class PadRosterTests
{
    // SDL lists pads per driver, and a batch found at once can list a later pad first. Ids count up as
    // SDL detects pads, and the earliest connected is the lowest, active first and next when it goes.
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
        Assert.Equal(3u, pads.ActiveId);

        pads.Sync([5], id => Open(id, opened), static _ => { });
        Assert.Equal(5u, pads.ActiveId);
    }

    private static Pad? Open(uint id, List<uint> opened)
    {
        opened.Add(id);

        return new Pad(id, nint.Zero, "pad", PadFamily.Generic);
    }
}
