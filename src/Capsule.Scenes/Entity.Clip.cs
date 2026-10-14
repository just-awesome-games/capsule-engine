using Capsule.UI;

namespace Capsule.Scenes;

// The nearest ClipRect above an entity, composed down the tree and cached with the same lazy staleness as
// the tint. The navigator and a headless run read it with no draw. The draw order is not its source.
public partial class Entity
{
    // The clip that cuts this entity's renderers and pointer hits, or null when nothing above clips. A clip
    // joins only a ScreenEntity. The composed reference lives on ScreenEntity alone and a plain entity
    // beneath one reads its nearest screen ancestor's. A world entity pays nothing for it.
    internal ClipRect? NearestClip
    {
        get
        {
            Entity? holder = this;
            while (holder is not null and not ScreenEntity)
            {
                holder = holder._parent;
            }

            return holder is ScreenEntity screen ? screen.ResolveClip() : null;
        }
    }

    // Whether this entity's composed clip is current. Adding or removing a clip above clears it, and so does
    // a parent change, and the next read recomposes it from the nearest fresh ancestor down. Invariant: a
    // stale screen entity's screen descendants are also stale.
    private bool ClipFresh
    {
        get => (_traits & Traits.ClipFresh) != 0;
        set => _traits = value ? _traits | Traits.ClipFresh : _traits & ~Traits.ClipFresh;
    }

    // Recomposes a stale clip from the entity's own components and then the clip above it.
    internal ClipRect? ResolveClip()
    {
        if (ClipFresh)
        {
            return ((ScreenEntity)this).TreeClip;
        }

        ClipRect? clip = null;
        foreach (Component component in Components)
        {
            if (component is ClipRect found)
            {
                clip = found;
                break;
            }
        }

        clip ??= _parent?.NearestClip;
        ((ScreenEntity)this).TreeClip = clip;
        ClipFresh = true;

        return clip;
    }

    // Called as a clip joins or leaves this entity. A screen entity that is already stale ends its walk,
    // because the invariant makes every screen entity beneath it stale too. A plain entity holds no clip and
    // never ends one.
    internal void StaleClip()
    {
        if (this is ScreenEntity)
        {
            if (!ClipFresh)
            {
                return;
            }

            ClipFresh = false;
        }

        foreach (Entity child in Children)
        {
            child.StaleClip();
        }
    }
}
