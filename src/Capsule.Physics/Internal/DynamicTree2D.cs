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
// little from being reinserted, insertion descends on the surface-area heuristic, and rebalancing uses
// rotations. The tree is never rebuilt wholesale and is deterministic for a given sequence of
// operations.
//
// Each layer's proxies form a tree of their own, rooted apart from the others in one shared node
// pool, and a walk descends only the roots its filter names. Bodies crowding round the surfaces they
// query, as riders on a platform, then cost that walk nothing. In one tree a subtree mixing both would
// carry both bits and could not be pruned. Every node of a layer's tree carries its layer bit.
internal sealed class DynamicTree2D
{
    internal const int NullNode = -1;

    private const int Layers = 64;

    // World units of slack around a proxy's tight bounds. Ordinary motion then costs a bounds write
    // instead of a reinsertion. The second is world units of lookahead per unit of displacement, and
    // the third caps the lookahead on each axis. A jump far past the cap is a teleport, not travel.
    private const float BoundsMargin = 2f;
    private const float DisplacementLookahead = 8f;
    private const float MaxLookahead = 32f;

    private Node[] _nodes = new Node[16];

    // A walk holds at most one waiting sibling per level below the root plus the root itself. The
    // tree's height bounds it. InsertLeaf grows it, and no walk does.
    private int[] _stack = new int[64];

    // Each layer's root, indexed by its bit, and the bits whose trees hold a proxy.
    private readonly int[] _roots = new int[Layers];
    private ulong _rooted;
    private int _freeList;

    internal DynamicTree2D()
    {
        Array.Fill(_roots, NullNode);
        FreeFrom(0);
    }

    internal int UserDataOf(int proxyId) => _nodes[proxyId].UserData;

    // Grows the node pool to hold proxies leaves and their branches. The added nodes queue behind the
    // free ones and are handed out in the order growing on demand would hand them out.
    internal void Reserve(int proxies)
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

        int proxyId = AllocateNode();
        _nodes[proxyId].Box = tight.Expanded(BoundsMargin);
        _nodes[proxyId].Mask = mask;
        _nodes[proxyId].UserData = userData;
        InsertLeaf(proxyId);

        return proxyId;
    }

    internal void DestroyProxy(int proxyId)
    {
        RemoveLeaf(proxyId);
        FreeNode(proxyId);
    }

    // Refits the proxy, reinserting it only when its tight bounds escape its fat ones. Returns whether
    // it reinserted.
    internal bool MoveProxy(int proxyId, in Aabb2D tight, Vector2 displacement)
    {
        if (_nodes[proxyId].Box.Contains(tight))
        {
            return false;
        }

        RemoveLeaf(proxyId);

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
        InsertLeaf(proxyId);

        return true;
    }

    // Moves a leaf to the tree of its new layer, a single bit, keeping its id and its fat bounds.
    internal void SetProxyMask(int proxyId, ulong mask)
    {
        RequireLayer(mask);
        if (_nodes[proxyId].Mask == mask)
        {
            return;
        }

        RemoveLeaf(proxyId);
        _nodes[proxyId].Mask = mask;
        InsertLeaf(proxyId);
    }

    // The layers' trees are walked in bit order, and each in the order a single tree is.
    internal void Query<TVisitor>(in Aabb2D box, ulong mask, ref TVisitor visitor)
        where TVisitor : struct, ITreeVisitor2D, allows ref struct
    {
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

    // The fraction a visitor narrows to holds into the next layer's tree.
    internal void RayCast<TVisitor>(Vector2 origin, Vector2 direction, float maxFraction, ulong mask, ref TVisitor visitor)
        where TVisitor : struct, IRayVisitor2D, allows ref struct
    {
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
        if (_freeList == NullNode)
        {
            int previous = _nodes.Length;
            Array.Resize(ref _nodes, previous * 2);
            FreeFrom(previous);
        }

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

    // Branch-and-bound descent on the surface-area heuristic. Descend while that costs less than
    // pairing the leaf with the node here.
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

        int deepest = _nodes[root].Height + 1;
        if (deepest > _stack.Length)
        {
            Array.Resize(ref _stack, deepest * 2);
        }
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
}
