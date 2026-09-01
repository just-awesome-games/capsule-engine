using System.Numerics;

namespace Capsule.Collision.Internal;

/// <summary>Visits proxies whose fat bounds overlap a query; returning false ends the walk.</summary>
internal interface ITreeVisitor
{
    bool Visit(int proxyId);
}

/// <summary>
/// Visits proxies a ray could reach, nearest first is not guaranteed; the returned value is the
/// new maximum fraction to keep searching within, and zero ends the walk.
/// </summary>
internal interface IRayVisitor
{
    float Visit(int proxyId, float maxFraction);
}

/// <summary>
/// A dynamic bounding-volume hierarchy over moving colliders: fat bounds so a proxy that moves a
/// little is not reinserted, surface-area-heuristic descent for insertion, and rotation-based
/// rebalancing. Never rebuilt wholesale, and deterministic for a given sequence of operations.
/// </summary>
internal sealed class DynamicTree
{
    internal const int NullNode = -1;

    // World units of slack around a proxy's tight bounds, so ordinary motion costs a bounds
    // write instead of a reinsertion, and world units of lookahead per unit of displacement.
    private const float BoundsMargin = 2f;
    private const float DisplacementLookahead = 2f;

    private Node[] _nodes;
    private int[] _stack = new int[64];
    private int _root = NullNode;
    private int _freeList;

    internal DynamicTree(int capacity = 16)
    {
        _nodes = new Node[Math.Max(capacity, 4)];
        FreeFrom(0);
    }

    internal int ProxyCount { get; private set; }

    internal int Height => _root == NullNode ? 0 : _nodes[_root].Height;

    internal Aabb2D BoundsOf(int proxyId) => _nodes[proxyId].Box;

    internal int UserDataOf(int proxyId) => _nodes[proxyId].UserData;

    internal int CreateProxy(in Aabb2D tight, int userData)
    {
        int proxyId = AllocateNode();
        _nodes[proxyId].Box = tight.Expanded(BoundsMargin);
        _nodes[proxyId].UserData = userData;
        _nodes[proxyId].Height = 0;
        InsertLeaf(proxyId);
        ProxyCount++;

        return proxyId;
    }

    internal void DestroyProxy(int proxyId)
    {
        RemoveLeaf(proxyId);
        FreeNode(proxyId);
        ProxyCount--;
    }

    /// <summary>Refits the proxy, reinserting it only when its tight bounds escape its fat ones.</summary>
    internal bool MoveProxy(int proxyId, in Aabb2D tight, Vector2 displacement)
    {
        if (_nodes[proxyId].Box.Contains(tight))
        {
            return false;
        }

        RemoveLeaf(proxyId);

        Aabb2D fat = tight.Expanded(BoundsMargin);

        // Predicted along the motion so a proxy travelling in one direction reinserts less often;
        // the slack is added on the side it is heading towards and nowhere else.
        Vector2 predicted = displacement * DisplacementLookahead;
        Vector2 min = fat.Min + Vector2.Min(predicted, Vector2.Zero);
        Vector2 max = fat.Max + Vector2.Max(predicted, Vector2.Zero);

        // The lookahead is slack, not geometry. A displacement large enough to carry the slack off
        // the end of the float range is dropped rather than stored: the tight box is what the
        // proxy has to cover, and an infinite bound here would union its way up the ancestors and
        // lose colliders that have nothing to do with this one.
        _nodes[proxyId].Box = IsFinite(min) && IsFinite(max) ? new Aabb2D(min, max) : fat;
        InsertLeaf(proxyId);

        return true;
    }

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    internal void Query<TVisitor>(in Aabb2D box, ref TVisitor visitor)
        where TVisitor : struct, ITreeVisitor, allows ref struct
    {
        if (_root == NullNode)
        {
            return;
        }

        int depth = 0;
        _stack[depth++] = _root;

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

    internal void RayCast<TVisitor>(Vector2 origin, Vector2 direction, float maxFraction, ref TVisitor visitor)
        where TVisitor : struct, IRayVisitor, allows ref struct
    {
        if (_root == NullNode)
        {
            return;
        }

        int depth = 0;
        _stack[depth++] = _root;

        while (depth > 0)
        {
            int nodeId = _stack[--depth];
            ref Node node = ref _nodes[nodeId];

            if (!Segments.IntersectsBox(node.Box, origin, direction, maxFraction))
            {
                continue;
            }

            if (node.IsLeaf)
            {
                float next = visitor.Visit(nodeId, maxFraction);
                if (next <= 0f)
                {
                    return;
                }

                maxFraction = next;
                continue;
            }

            depth = Push(depth, node.Child1, node.Child2);
        }
    }

    private int Push(int depth, int child1, int child2)
    {
        if (depth + 2 > _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

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
            Height = 0,
            UserData = -1,
        };
        return nodeId;
    }

    private void FreeNode(int nodeId)
    {
        // Parent doubles as the free-list link: a node is on exactly one of the two lists.
        _nodes[nodeId].Parent = _freeList;
        _nodes[nodeId].Height = -1;
        _freeList = nodeId;
    }

    private void FreeFrom(int first)
    {
        for (int index = first; index < _nodes.Length - 1; index++)
        {
            _nodes[index].Parent = index + 1;
            _nodes[index].Height = -1;
        }

        _nodes[^1].Parent = NullNode;
        _nodes[^1].Height = -1;
        _freeList = first;
    }

    // Branch-and-bound descent on the surface-area heuristic: at each node, keep descending only
    // while doing so costs less than making the sibling a leaf's new brother here.
    private void InsertLeaf(int leaf)
    {
        if (_root == NullNode)
        {
            _root = leaf;
            _nodes[leaf].Parent = NullNode;
            return;
        }

        Aabb2D leafBox = _nodes[leaf].Box;
        int index = _root;
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
        _nodes[newParent].Height = _nodes[sibling].Height + 1;

        if (oldParent != NullNode)
        {
            if (_nodes[oldParent].Child1 == sibling)
            {
                _nodes[oldParent].Child1 = newParent;
            }
            else
            {
                _nodes[oldParent].Child2 = newParent;
            }
        }
        else
        {
            _root = newParent;
        }

        _nodes[newParent].Child1 = sibling;
        _nodes[newParent].Child2 = leaf;
        _nodes[sibling].Parent = newParent;
        _nodes[leaf].Parent = newParent;

        Refit(_nodes[leaf].Parent);
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
        if (leaf == _root)
        {
            _root = NullNode;
            return;
        }

        int parent = _nodes[leaf].Parent;
        int grandParent = _nodes[parent].Parent;
        int sibling = _nodes[parent].Child1 == leaf ? _nodes[parent].Child2 : _nodes[parent].Child1;

        if (grandParent != NullNode)
        {
            if (_nodes[grandParent].Child1 == parent)
            {
                _nodes[grandParent].Child1 = sibling;
            }
            else
            {
                _nodes[grandParent].Child2 = sibling;
            }

            _nodes[sibling].Parent = grandParent;
            FreeNode(parent);
            Refit(grandParent);
        }
        else
        {
            _root = sibling;
            _nodes[sibling].Parent = NullNode;
            FreeNode(parent);
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

    // One AVL rotation where a subtree leans by more than one level, which is what keeps query
    // depth logarithmic without ever rebuilding the tree.
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

        int oldParent = _nodes[iPivot].Parent;
        if (oldParent != NullNode)
        {
            if (_nodes[oldParent].Child1 == iA)
            {
                _nodes[oldParent].Child1 = iPivot;
            }
            else
            {
                _nodes[oldParent].Child2 = iPivot;
            }
        }
        else
        {
            _root = iPivot;
        }

        // The taller grandchild rises with the pivot; the shorter one takes the rotated node's
        // free slot, which is where the height difference is spent.
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
        internal int UserData;
        internal int Parent;
        internal int Child1;
        internal int Child2;
        internal int Height;

        internal readonly bool IsLeaf => Child1 == NullNode;
    }
}
