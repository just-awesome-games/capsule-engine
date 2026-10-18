using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

public sealed class TexelPoolTests
{
    [Fact]
    public void TheTexelPool_KeepsItsLargestBuffersUpToTheDecodeConcurrency()
    {
        TexelPool pool = new();
        byte[][] pages = [.. Enumerable.Range(1, AssetDecodes.Concurrency).Select(static size => new byte[100_000 * size])];
        foreach (byte[] page in pages)
        {
            pool.Return(page);
        }

        pool.Return(new byte[84_999]);
        pool.Return(new byte[99_999]);
        byte[] largest = new byte[1_000_000];
        pool.Return(largest);

        Assert.Equal(AssetDecodes.Concurrency, pool.Count);
        Assert.Same(largest, pool.Rent(largest.Length));
        Assert.NotSame(pages[0], pool.Rent(pages[0].Length));
    }
}
