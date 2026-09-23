using Capsule.Rendering;

namespace Capsule.Scenes;

// Whether the entity draws and the colour it draws with, each composed down the tree and cached with
// the same lazy staleness as the world transform.
public partial class Entity
{
    // The tint and the visibility composed with every ancestor's. A write above marks them stale, and the
    // next read recomposes them from the nearest valid ancestor down. Invariant: a stale entity's
    // descendants are also stale.
    private ColorRgba _treeTint = ColorRgba.White;
    private bool _shownInTree = true;
    private bool _appearanceStale = true;

    /// <summary>
    /// Whether this entity and its subtree draw. A hidden entity still steps, collides, plays sound and
    /// reports to its notifiers, and a <see cref="UI.Focusable"/> in its subtree takes no focus.
    /// </summary>
    public bool Visible
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            StaleAppearance();
        }
    } = true;

    /// <summary>
    /// The colour multiplied into every renderer beneath this entity, alpha included, after each
    /// ancestor's, where the default white changes nothing.
    /// </summary>
    public ColorRgba Tint
    {
        get;

        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            StaleAppearance();
        }
    } = ColorRgba.White;

    // Whether this entity and every ancestor is visible. Focus reads this and not the tint, so a faded
    // item can still hold the focus.
    internal bool ShownInTree
    {
        get
        {
            ResolveAppearance();
            return _shownInTree;
        }
    }

    // Whether this entity's renderers draw, and the tint they draw with. A hidden entity and a fully
    // faded one both draw nothing.
    internal bool TryGetDrawTint(out ColorRgba tint)
    {
        ResolveAppearance();
        tint = _treeTint;

        return _shownInTree && tint.A != 0;
    }

    private void ResolveAppearance()
    {
        if (!_appearanceStale)
        {
            return;
        }

        if (_parent is { } parent)
        {
            parent.ResolveAppearance();
            _shownInTree = Visible && parent._shownInTree;
            _treeTint = Tint == ColorRgba.White ? parent._treeTint : ColorRgba.Multiply(parent._treeTint, Tint);
        }
        else
        {
            _shownInTree = Visible;
            _treeTint = Tint;
        }

        _appearanceStale = false;
    }

    // Stops at an entity already stale, because the invariant makes everything beneath it stale too.
    // A parent change re-stales the subtree through the transform's full walk instead.
    private void StaleAppearance()
    {
        if (_appearanceStale)
        {
            return;
        }

        _appearanceStale = true;

        foreach (Entity child in Children)
        {
            child.StaleAppearance();
        }
    }
}
