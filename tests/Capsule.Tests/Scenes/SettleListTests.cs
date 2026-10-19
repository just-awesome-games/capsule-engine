using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class SettleListTests
{
    // A walk visits what it began with in add order and skips an entry removed before its turn. An
    // entry added during the walk, or removed and added back, waits for the next walk at the end. The
    // next walk keeps the survivors' order.
    [Fact]
    public void AWalkChangedByItsCallbacks_VisitsTheSurvivorsInOrder_AndTheNextWalkSeesTheAdds()
    {
        Entry[] entries = [new("a"), new("b"), new("c"), new("d"), new("e")];
        Entry late = new("late");
        SettleList<Entry> list = new();
        foreach (Entry entry in entries)
        {
            list.Add(entry);
        }

        List<string> visited = [];
        list.Begin();
        while (list.TryNext(out Entry? entry))
        {
            visited.Add(entry.Name);
            if (entry.Name == "b")
            {
                list.Remove(entries[0]);
                list.Remove(entries[3]);
                list.Add(late);
                list.Remove(entries[1]);
                list.Add(entries[1]);
            }
        }

        list.End();
        Assert.Equal(["a", "b", "c", "e"], visited);
        Assert.Equal(4, list.Count);

        Assert.Equal(["c", "e", "late", "b"], Walk(list));
    }

    // A removal names an entry by the slot the list wrote on it. An entry the list no longer holds, or
    // never held, is ignored even when another entry has since taken that slot.
    [Fact]
    public void RemovingAnEntryTheListDoesNotHold_ChangesNothing()
    {
        Entry first = new("first");
        Entry second = new("second");
        Entry stranger = new("stranger");
        SettleList<Entry> list = new();
        list.Add(first);
        list.Add(second);
        list.Remove(first);
        Assert.Equal(["second"], Walk(list));

        list.Remove(first);
        list.Remove(stranger);

        Assert.Equal(["second"], Walk(list));
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void ClearingDuringAWalk_EndsIt()
    {
        SettleList<Entry> list = new();
        list.Add(new Entry("a"));
        list.Add(new Entry("b"));

        List<string> visited = [];
        list.Begin();
        while (list.TryNext(out Entry? entry))
        {
            visited.Add(entry.Name);
            list.Clear();
        }

        list.End();
        Assert.Equal(["a"], visited);
        Assert.Equal(0, list.Count);
    }

    private static List<string> Walk(SettleList<Entry> list)
    {
        List<string> visited = [];
        list.Begin();
        while (list.TryNext(out Entry? entry))
        {
            visited.Add(entry.Name);
        }

        list.End();
        return visited;
    }

    private sealed class Entry(string name) : ISettleListEntry
    {
        public string Name { get; } = name;

        public int SettleSlot { get; set; }
    }
}
