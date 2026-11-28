using System.Numerics;

namespace Capsule.Physics.Internal;

// Visits proxies whose fat bounds overlap a query. Return false to end the walk.
internal interface ITreeVisitor2D
{
    bool Visit(int proxyId);
}

// Visits proxies a ray could reach, in no particular order. The return value is the new maximum
// fraction to search within. The walk goes on at zero, because every proxy the ray starts inside
// meets it there and the visitor has to see them all to pick among them.
internal interface IRayVisitor2D
{
    float Visit(int proxyId, float maxFraction);
}

// A dynamic bounding-volume hierarchy over moving colliders. Fat bounds keep a proxy that moves a
// little from being reinserted. Insertion descends greedily by surface-area cost, and AVL rotations
// rebalance the branches it passes. The tree is deterministic for a given sequence of operations, and
// no query's results depend on its arrangement.
//
// A layer is indexed, holding a tree, or unindexed. A proxy created on or moved onto an indexed layer
// is inserted at once. One on an unindexed layer is loose and holds only its fat bounds. Moving it
// writes them, and removing it unlinks it. The first walk naming an unindexed layer that holds loose
// proxies builds them top-down into its tree, as when a scene's start makes a crowd at once, and the
// layer is indexed from then on. A layer stays indexed when it empties. A layer that churns after its
// first walk pays one insertion per proxy and never a rebuild.
//
// Each layer's proxies form a tree of their own, rooted apart from the others in one shared node
// pool, and a walk descends only the roots its filter names. Bodies crowding round the surfaces they
// query, as riders on a platform, then cost that walk nothing. In one tree a subtree mixing both would
// carry both bits and could not be pruned. Every node of a layer's tree carries its layer bit.
internal sealed class DynamicTree2D
{
    internal const int NullNode = -1;

    private const int Layers = 64;

    // The height of a loose proxy. A free node's is -1 and a leaf's 0.
    private const int Loose = -2;

    // The cells a build's curve divides the longer side of its centres' bounds into, the most 16 bits
    // of each axis hold.
    private const float CurveCells = ushort.MaxValue;

    // World units of slack around a proxy's tight bounds. Ordinary motion then costs a bounds write
    // instead of a reinsertion. The second is world units of lookahead per unit of displacement, and
    // the third caps the lookahead on each axis. A jump far past the cap is a teleport, not travel.
    private const float BoundsMargin = 2f;
    private const float DisplacementLookahead = 8f;
    private const float MaxLookahead = 32f;

    private Node[] _nodes = new Node[16];

    // A walk holds at most one waiting sibling per level below the root plus the root itself. The
    // tree's height bounds it. InsertLeaf and BuildLayer grow it, and no walk does.
    private int[] _stack = new int[64];

    // Each layer's root, indexed by its bit, and the bits whose trees hold a proxy.
    private readonly int[] _roots = new int[Layers];
    private ulong _rooted;
    private int _freeList;

    // How many proxies the tree holds. The node pool keeps room for twice as many, more than their
    // leaves and branches take, and no build or insertion grows it.
    private int _proxies;

    // Each unindexed layer's loose proxies, indexed by its bit. A list runs from its head through
    // Child2, and back through Parent, which a loose proxy otherwise leaves unused.
    private readonly int[] _loose = new int[Layers];

    // A build's proxies, each its Morton code above its id, sorted through the second array by radix.
    // Both hold every proxy while any is loose.
    private ulong[] _order = new ulong[16];
    private ulong[] _sorting = new ulong[16];

    // The bits of the layers holding a tree, and of those holding a loose proxy.
    internal ulong IndexedLayers { get; private set; }

    internal ulong LooseLayers { get; private set; }

    internal DynamicTree2D()
    {
        Array.Fill(_roots, NullNode);
        Array.Fill(_loose, NullNode);
        FreeFrom(0);
    }

    internal int UserDataOf(int proxyId) => _nodes[proxyId].UserData;

    // Grows the node pool and a build's sort to hold proxies.
    internal void Reserve(int proxies)
    {
        if (_order.Length < proxies)
        {
            _order = new ulong[proxies];
            _sorting = new ulong[proxies];
        }

        GrowNodes(proxies);
    }

    // Grows the node pool to hold proxies leaves and their branches. The added nodes queue behind the
    // free ones and are handed out in the order growing on demand would hand them out.
    private void GrowNodes(int proxies)
    {
        int previous = _nodes.Length;
        int length = previous;
        while (length < proxies * 2)
        {
            length *= 2;
        }

        if (length == previous)
        {
            return;
        }

        Array.Resize(ref _nodes, length);
        Chain(previous);
        if (_freeList == NullNode)
        {
            _freeList = previous;
            return;
        }

        int tail = _freeList;
        while (_nodes[tail].Parent != NullNode)
        {
            tail = _nodes[tail].Parent;
        }

        _nodes[tail].Parent = previous;
    }

    internal Aabb2D FatBoxOf(int proxyId) => _nodes[proxyId].Box;

    // mask is the proxy's layer, a single bit.
    internal int CreateProxy(in Aabb2D tight, int userData, ulong mask)
    {
        RequireLayer(mask);

        _proxies++;
        GrowNodes(_proxies);
        int proxyId = AllocateNode();
        _nodes[proxyId].Box = tight.Expanded(BoundsMargin);
        _nodes[proxyId].Mask = mask;
        _nodes[proxyId].UserData = userData;
        Place(proxyId);

        return proxyId;
    }

    internal void DestroyProxy(int proxyId)
    {
        Detach(proxyId);
        FreeNode(proxyId);
        _proxies--;
    }

    // Refits the proxy, reinserting it only when its tight bounds escape its fat ones. Returns whether
    // it reinserted.
    internal bool MoveProxy(int proxyId, in Aabb2D tight, Vector2 displacement)
    {
        if (_nodes[proxyId].Box.Contains(tight))
        {
            return false;
        }

        bool loose = _nodes[proxyId].Height == Loose;
        if (!loose)
        {
            RemoveLeaf(proxyId);
        }

        Aabb2D fat = tight.Expanded(BoundsMargin);

        // Slack on the side the proxy is heading towards, which cuts reinsertions for steady travel.
        // The cap also keeps a displacement that overflowed to infinity finite. An infinite bound
        // would union its way up the ancestors and lose unrelated colliders.
        Vector2 predicted = Vector2.Clamp(
            displacement * DisplacementLookahead,
            new Vector2(-MaxLookahead),
            new Vector2(MaxLookahead));
        _nodes[proxyId].Box = new Aabb2D(
            fat.Min + Vector2.Min(predicted, Vector2.Zero),
            fat.Max + Vector2.Max(predicted, Vector2.Zero));
        if (!loose)
        {
            InsertLeaf(proxyId);
        }

        return true;
    }

    // Moves a proxy to its new layer, a single bit, keeping its id and its fat bounds.
    internal void SetProxyMask(int proxyId, ulong mask)
    {
        RequireLayer(mask);
        if (_nodes[proxyId].Mask == mask)
        {
            return;
        }

        Detach(proxyId);
        _nodes[proxyId].Mask = mask;
        Place(proxyId);
    }

    // Whether any of the mask's layers has a proxy.
    internal bool Holds(ulong mask) => (mask & (_rooted | LooseLayers)) != 0;

    // Whether any proxy on the mask's layers has fat bounds that meet the box.
    internal bool Overlaps(in Aabb2D box, ulong mask)
    {
        AnyVisitor visitor = default;
        Query(box, mask, ref visitor);

        return visitor.Found;
    }

    // The layers' trees are walked in bit order, and each in the order a single tree is.
    internal void Query<TVisitor>(in Aabb2D box, ulong mask, ref TVisitor visitor)
        where TVisitor : struct, ITreeVisitor2D, allows ref struct
    {
        if ((mask & LooseLayers) != 0)
        {
            Build(mask & LooseLayers);
        }

        for (ulong layers = mask & _rooted; layers != 0; layers &= layers - 1)
        {
            int depth = 0;
            _stack[depth++] = _roots[BitOperations.TrailingZeroCount(layers)];

            while (depth > 0)
            {
                int nodeId = _stack[--depth];
                ref Node node = ref _nodes[nodeId];
                if (!node.Box.Overlaps(box))
                {
                    continue;
                }

                if (node.IsLeaf)
                {
                    if (!visitor.Visit(nodeId))
                    {
                        return;
                    }

                    continue;
                }

                depth = Push(depth, node.Child1, node.Child2);
            }
        }
    }

    // Walks each masked layer's tree in turn. A fraction the visitor narrows to in one tree bounds the
    // walk of the next.
    internal void RayCast<TVisitor>(Vector2 origin, Vector2 direction, float maxFraction, ulong mask, ref TVisitor visitor)
        where TVisitor : struct, IRayVisitor2D, allows ref struct
    {
        if ((mask & LooseLayers) != 0)
        {
            Build(mask & LooseLayers);
        }

        for (ulong layers = mask & _rooted; layers != 0; layers &= layers - 1)
        {
            int depth = 0;
            _stack[depth++] = _roots[BitOperations.TrailingZeroCount(layers)];

            while (depth > 0)
            {
                int nodeId = _stack[--depth];
                ref Node node = ref _nodes[nodeId];

                if (!Rays2D.RayBox(node.Box, origin, direction, maxFraction, out _, out _))
                {
                    continue;
                }

                if (node.IsLeaf)
                {
                    maxFraction = visitor.Visit(nodeId, maxFraction);
                    continue;
                }

                depth = Push(depth, node.Child1, node.Child2);
            }
        }
    }

    // Builds each layer of layers, which all hold loose proxies, into its tree and indexes it.
    private void Build(ulong layers)
    {
        for (; layers != 0; layers &= layers - 1)
        {
            BuildLayer(BitOperations.TrailingZeroCount(layers));
        }
    }

    // Builds the loose proxies of the layer at bit into its tree. The proxies are sorted along a
    // Z-order curve through their centres, scaled alike on both axes, and each branch splits its run
    // where the codes first differ. A branch then splits its square of the curve in half across its
    // longer side, and codes that tie split at the middle of their run.
    private void BuildLayer(int bit)
    {
        Vector2 low = new(float.PositiveInfinity);
        Vector2 high = new(float.NegativeInfinity);
        for (int leaf = _loose[bit]; leaf != NullNode; leaf = _nodes[leaf].Child2)
        {
            Vector2 centre = Centre(leaf);
            low = Vector2.Min(low, centre);
            high = Vector2.Max(high, centre);
        }

        float span = MathF.Max(high.X - low.X, high.Y - low.Y);
        float scale = span > 0f ? CurveCells / span : 0f;
        int count = 0;
        for (int leaf = _loose[bit]; leaf != NullNode;)
        {
            int next = _nodes[leaf].Child2;
            _nodes[leaf].Child2 = NullNode;
            _nodes[leaf].Height = 0;

            Vector2 cell = Vector2.Min((Centre(leaf) - low) * scale, new Vector2(CurveCells));
            ulong code = Interleave((uint)cell.X) | (Interleave((uint)cell.Y) << 1);
            _order[count++] = (code << 32) | (uint)leaf;
            leaf = next;
        }

        SortByCode(count);

        int root = BuildSorted(0, count - 1);
        _nodes[root].Parent = NullNode;
        _roots[bit] = root;
        _loose[bit] = NullNode;

        ulong layer = 1UL << bit;
        LooseLayers &= ~layer;
        IndexedLayers |= layer;
        _rooted |= layer;
        GrowStack(_nodes[root].Height + 1);
    }

    // Twice a node's centre, which orders and scales as the centre itself does.
    private Vector2 Centre(int nodeId) => _nodes[nodeId].Box.Min + _nodes[nodeId].Box.Max;

    // Spreads the low 16 bits of value to the even bits of the result.
    private static ulong Interleave(uint value)
    {
        value = (value | (value << 8)) & 0x00FF00FFu;
        value = (value | (value << 4)) & 0x0F0F0F0Fu;
        value = (value | (value << 2)) & 0x33333333u;
        value = (value | (value << 1)) & 0x55555555u;

        return value;
    }

    // A stable radix sort of the first count entries of _order by their codes, a byte a pass. The four
    // passes leave the result back in _order.
    private void SortByCode(int count)
    {
        Span<int> starts = stackalloc int[256];
        ulong[] from = _order;
        ulong[] to = _sorting;
        for (int shift = 32; shift < 64; shift += 8)
        {
            starts.Clear();
            for (int index = 0; index < count; index++)
            {
                starts[(int)(from[index] >> shift) & 0xFF]++;
            }

            int total = 0;
            for (int bucket = 0; bucket < starts.Length; bucket++)
            {
                (starts[bucket], total) = (total, total + starts[bucket]);
            }

            for (int index = 0; index < count; index++)
            {
                ulong entry = from[index];
                to[starts[(int)(entry >> shift) & 0xFF]++] = entry;
            }

            (from, to) = (to, from);
        }
    }

    // Builds the sorted run from first to last into a subtree and returns its root. The split is the
    // last entry sharing more leading code bits with the first than the last one does.
    private int BuildSorted(int first, int last)
    {
        if (first == last)
        {
            return (int)(uint)_order[first];
        }

        uint firstCode = (uint)(_order[first] >> 32);
        int shared = BitOperations.LeadingZeroCount(firstCode ^ (uint)(_order[last] >> 32));
        int split = (first + last) >>> 1;
        if (shared < 32)
        {
            split = first;
            for (int step = last - first; step > 1;)
            {
                step = (step + 1) >> 1;
                int candidate = split + step;
                if (candidate < last && BitOperations.LeadingZeroCount(firstCode ^ (uint)(_order[candidate] >> 32)) > shared)
                {
                    split = candidate;
                }
            }
        }

        int child1 = BuildSorted(first, split);
        int child2 = BuildSorted(split + 1, last);

        int parent = AllocateNode();
        _nodes[parent].Box = _nodes[child1].Box.Union(_nodes[child2].Box);
        _nodes[parent].Mask = _nodes[child1].Mask;
        _nodes[parent].Height = 1 + Math.Max(_nodes[child1].Height, _nodes[child2].Height);
        _nodes[parent].Child1 = child1;
        _nodes[parent].Child2 = child2;
        _nodes[child1].Parent = parent;
        _nodes[child2].Parent = parent;

        return parent;
    }

    // Inserts a proxy into its layer's tree when the layer is indexed, and otherwise links it at the
    // head of the layer's loose list.
    private void Place(int proxyId)
    {
        ref Node node = ref _nodes[proxyId];
        if ((node.Mask & IndexedLayers) != 0)
        {
            node.Height = 0;
            node.Child2 = NullNode;
            InsertLeaf(proxyId);
            return;
        }

        if (_order.Length < _proxies)
        {
            _order = new ulong[_nodes.Length / 2];
            _sorting = new ulong[_nodes.Length / 2];
        }

        ref int head = ref _loose[BitOperations.TrailingZeroCount(node.Mask)];
        node.Height = Loose;
        node.Parent = NullNode;
        node.Child2 = head;
        if (head != NullNode)
        {
            _nodes[head].Parent = proxyId;
        }

        head = proxyId;
        LooseLayers |= node.Mask;
    }

    // Takes a proxy out of its layer's tree, or unlinks it from its loose list.
    private void Detach(int proxyId)
    {
        ref Node node = ref _nodes[proxyId];
        if (node.Height != Loose)
        {
            RemoveLeaf(proxyId);
            return;
        }

        ref int head = ref _loose[BitOperations.TrailingZeroCount(node.Mask)];
        if (node.Parent == NullNode)
        {
            head = node.Child2;
        }
        else
        {
            _nodes[node.Parent].Child2 = node.Child2;
        }

        if (node.Child2 != NullNode)
        {
            _nodes[node.Child2].Parent = node.Parent;
        }

        if (head == NullNode)
        {
            LooseLayers &= ~node.Mask;
        }
    }

    private void GrowStack(int deepest)
    {
        if (deepest > _stack.Length)
        {
            Array.Resize(ref _stack, deepest * 2);
        }
    }

    private static void RequireLayer(ulong mask)
    {
        if (BitOperations.PopCount(mask) != 1)
        {
            throw new ArgumentException($"A proxy's layer is a single bit, not 0x{mask:x}. Pass the bits of one layer's filter.", nameof(mask));
        }
    }

    // The root slot of the tree a node belongs to, by the layer bit every node of that tree carries.
    private ref int RootOf(int nodeId) => ref _roots[BitOperations.TrailingZeroCount(_nodes[nodeId].Mask)];

    private int Push(int depth, int child1, int child2)
    {
        _stack[depth++] = child1;
        _stack[depth++] = child2;

        return depth;
    }

    private int AllocateNode()
    {
        int nodeId = _freeList;
        _freeList = _nodes[nodeId].Parent;
        _nodes[nodeId] = new Node
        {
            Parent = NullNode,
            Child1 = NullNode,
            Child2 = NullNode,
        };

        return nodeId;
    }

    private void FreeNode(int nodeId)
    {
        // Parent doubles as the free-list link, since a node is on one list or the other.
        _nodes[nodeId].Parent = _freeList;
        _nodes[nodeId].Height = -1;
        _freeList = nodeId;
    }

    private void FreeFrom(int first)
    {
        Chain(first);
        _freeList = first;
    }

    // Links the nodes from first to the end into a free chain, in ascending order.
    private void Chain(int first)
    {
        for (int index = first; index < _nodes.Length - 1; index++)
        {
            _nodes[index].Parent = index + 1;
            _nodes[index].Height = -1;
        }

        _nodes[^1].Parent = NullNode;
        _nodes[^1].Height = -1;
    }

    // Greedy descent on the surface-area heuristic. At each branch the leaf stops and pairs with it when
    // that costs less than descending into either child, and otherwise descends into the cheaper one.
    // Refit then rebalances the branches above the new parent with AVL rotations.
    private void InsertLeaf(int leaf)
    {
        ref int root = ref RootOf(leaf);
        if (root == NullNode)
        {
            root = leaf;
            _nodes[leaf].Parent = NullNode;
            _rooted |= _nodes[leaf].Mask;
            return;
        }

        Aabb2D leafBox = _nodes[leaf].Box;
        int index = root;
        while (!_nodes[index].IsLeaf)
        {
            int child1 = _nodes[index].Child1;
            int child2 = _nodes[index].Child2;

            float area = _nodes[index].Box.Perimeter;
            float combinedArea = _nodes[index].Box.Union(leafBox).Perimeter;

            float cost = 2f * combinedArea;
            float inheritanceCost = 2f * (combinedArea - area);

            float cost1 = DescentCost(child1, leafBox, inheritanceCost);
            float cost2 = DescentCost(child2, leafBox, inheritanceCost);

            if (cost < cost1 && cost < cost2)
            {
                break;
            }

            index = cost1 < cost2 ? child1 : child2;
        }

        int sibling = index;
        int oldParent = _nodes[sibling].Parent;
        int newParent = AllocateNode();
        _nodes[newParent].Parent = oldParent;
        _nodes[newParent].Box = _nodes[sibling].Box.Union(leafBox);
        _nodes[newParent].Mask = _nodes[leaf].Mask;
        _nodes[newParent].Height = _nodes[sibling].Height + 1;
        ReplaceChild(oldParent, sibling, newParent);
        _nodes[newParent].Child1 = sibling;
        _nodes[newParent].Child2 = leaf;
        _nodes[sibling].Parent = newParent;
        _nodes[leaf].Parent = newParent;

        Refit(_nodes[leaf].Parent);
        GrowStack(_nodes[root].Height + 1);
    }

    private float DescentCost(int child, in Aabb2D leafBox, float inheritanceCost)
    {
        Aabb2D combined = _nodes[child].Box.Union(leafBox);

        return _nodes[child].IsLeaf
            ? combined.Perimeter + inheritanceCost
            : combined.Perimeter - _nodes[child].Box.Perimeter + inheritanceCost;
    }

    private void RemoveLeaf(int leaf)
    {
        ref int root = ref RootOf(leaf);
        if (leaf == root)
        {
            root = NullNode;
            _rooted &= ~_nodes[leaf].Mask;
            return;
        }

        int parent = _nodes[leaf].Parent;
        int grandParent = _nodes[parent].Parent;
        int sibling = _nodes[parent].Child1 == leaf ? _nodes[parent].Child2 : _nodes[parent].Child1;

        ReplaceChild(grandParent, parent, sibling);
        _nodes[sibling].Parent = grandParent;
        FreeNode(parent);
        Refit(grandParent);
    }

    // Points parent, or child's root when parent is null, at replacement where it pointed at child.
    private void ReplaceChild(int parent, int child, int replacement)
    {
        if (parent == NullNode)
        {
            RootOf(child) = replacement;
        }
        else if (_nodes[parent].Child1 == child)
        {
            _nodes[parent].Child1 = replacement;
        }
        else
        {
            _nodes[parent].Child2 = replacement;
        }
    }

    private void Refit(int from)
    {
        int index = from;
        while (index != NullNode)
        {
            index = Balance(index);

            int child1 = _nodes[index].Child1;
            int child2 = _nodes[index].Child2;
            _nodes[index].Height = 1 + Math.Max(_nodes[child1].Height, _nodes[child2].Height);
            _nodes[index].Box = _nodes[child1].Box.Union(_nodes[child2].Box);

            index = _nodes[index].Parent;
        }
    }

    // One AVL rotation where a subtree leans by more than one level, which keeps query depth
    // logarithmic.
    private int Balance(int iA)
    {
        ref Node a = ref _nodes[iA];
        if (a.IsLeaf || a.Height < 2)
        {
            return iA;
        }

        int iB = a.Child1;
        int iC = a.Child2;
        int balance = _nodes[iC].Height - _nodes[iB].Height;

        if (balance > 1)
        {
            return Rotate(iA, iC, iB);
        }

        if (balance < -1)
        {
            return Rotate(iA, iB, iC);
        }

        return iA;
    }

    private int Rotate(int iA, int iPivot, int iKeep)
    {
        int iF = _nodes[iPivot].Child1;
        int iG = _nodes[iPivot].Child2;

        _nodes[iPivot].Child1 = iA;
        _nodes[iPivot].Parent = _nodes[iA].Parent;
        _nodes[iA].Parent = iPivot;

        ReplaceChild(_nodes[iPivot].Parent, iA, iPivot);

        // The taller grandchild rises with the pivot. The shorter takes the rotated node's free slot,
        // spending the height difference.
        int iTall = _nodes[iF].Height > _nodes[iG].Height ? iF : iG;
        int iShort = iTall == iF ? iG : iF;

        _nodes[iPivot].Child2 = iTall;
        if (_nodes[iA].Child1 == iPivot)
        {
            _nodes[iA].Child1 = iShort;
        }
        else
        {
            _nodes[iA].Child2 = iShort;
        }

        _nodes[iShort].Parent = iA;

        _nodes[iA].Box = _nodes[iKeep].Box.Union(_nodes[iShort].Box);
        _nodes[iPivot].Box = _nodes[iA].Box.Union(_nodes[iTall].Box);
        _nodes[iA].Height = 1 + Math.Max(_nodes[iKeep].Height, _nodes[iShort].Height);
        _nodes[iPivot].Height = 1 + Math.Max(_nodes[iA].Height, _nodes[iTall].Height);

        return iPivot;
    }

    private struct Node
    {
        internal Aabb2D Box;
        internal ulong Mask;
        internal int Parent;
        internal int Child1;
        internal int Child2;
        internal int UserData;
        internal int Height;

        internal readonly bool IsLeaf => Child1 == NullNode;
    }

    // Ends a query at the first proxy it meets.
    private struct AnyVisitor : ITreeVisitor2D
    {
        internal bool Found;

        public bool Visit(int proxyId)
        {
            Found = true;

            return false;
        }
    }
}
