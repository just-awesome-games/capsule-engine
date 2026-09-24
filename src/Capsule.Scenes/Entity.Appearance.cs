using Capsule.Rendering;

namespace Capsule.Scenes;

// Whether the entity draws and the colour and flash it draws with, each composed down the tree and
// cached with the same lazy staleness as the world transform.
public partial class Entity
{
    // The tint and the visibility composed with every ancestor's. A write above marks them stale, and the
    // next read recomposes them from the nearest valid ancestor down. Invariant: a stale entity's
    // descendants are also stale.
    private ColorRgba _treeTint = ColorRgba.White;
    private bool _shownInTree = true;

    // The flash composed with every ancestor's, the colour in RGB and the amount in alpha.
    private ColorRgba _treeFlash;

    private bool _appearanceStale = true;

    /// <summary>
    /// Whether this entity and its subtree draw, defaulting to true. A hidden entity still steps, collides, plays sound and
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

    /// <summary>
    /// How far every sprite drawn beneath this entity is mixed towards <see cref="FlashColor"/>, from
    /// 0 to 1, where the default 0 changes nothing. Lights and lines do not flash.
    /// </summary>
    /// <remarks>
    /// The mix applies after <see cref="Tint"/> and after any material's own shader, so 1 draws each
    /// sprite as a silhouette of the flash colour and fades with the tint's alpha. An ancestor's flash
    /// applies over this entity's.
    /// </remarks>
    public float Flash
    {
        get;

        set
        {
            Guard.InUnit(value, nameof(value));
            if (field == value)
            {
                return;
            }

            field = value;
            StaleAppearance();
        }
    }

    /// <summary>The colour <see cref="Flash"/> mixes towards, white by default. Its alpha is ignored.</summary>
    public ColorRgba FlashColor
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

    // Whether this entity's renderers draw, and the tint and flash they draw with. The flash is the colour
    // in RGB and the amount in alpha, transparent when nothing here or above flashes. A hidden entity and
    // a fully faded one both draw nothing.
    internal bool TryGetDrawStyle(out ColorRgba tint, out ColorRgba flash)
    {
        ResolveAppearance();
        tint = _treeTint;
        flash = _treeFlash;

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
            _treeFlash = ComposeFlash(parent._treeFlash);
        }
        else
        {
            _shownInTree = Visible;
            _treeTint = Tint;
            _treeFlash = ComposeFlash(default);
        }

        _appearanceStale = false;
    }

    // This entity's mix then the parent's over it, which is one mix: the amount left unmixed is the
    // product of what each leaves, and the colour is each colour weighted by what it contributes.
    private ColorRgba ComposeFlash(ColorRgba parent)
    {
        if (Flash == 0f)
        {
            return parent;
        }

        if (parent.A == 0)
        {
            return FlashColor with { A = Unit(Flash) };
        }

        float above = parent.A / 255f;
        float amount = 1f - ((1f - Flash) * (1f - above));
        float own = Flash * (1f - above) / amount;
        float inherited = above / amount;

        return new ColorRgba(
            Unit(((FlashColor.R * own) + (parent.R * inherited)) / 255f),
            Unit(((FlashColor.G * own) + (parent.G * inherited)) / 255f),
            Unit(((FlashColor.B * own) + (parent.B * inherited)) / 255f),
            Unit(amount));
    }

    private static byte Unit(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

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
