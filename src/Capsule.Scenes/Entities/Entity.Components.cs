using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Capsule.Rendering;

namespace Capsule.Scenes;

// The components: attaching and detaching them, finding them here or up the ancestry, and the walk the
// hooks visit them by.
public partial class Entity
{
    internal ReadOnlySpan<Component> Components => CollectionsMarshal.AsSpan(_components);

    // Walks the component list. A hook may detach components, and the walk still visits each one after
    // the one it last visited. A component attached once that one and every later one have gone may be
    // skipped until the next walk.
    private ComponentWalk LiveComponents => new(_components);

    /// <summary>Attaches <paramref name="component"/>, which no entity may already own, and returns it.</summary>
    /// <typeparam name="T">The component's type, which the call site keeps.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// The component is already attached to an entity, or this entity refuses it. An entity refuses
    /// a second <see cref="Physics.KinematicBody2D"/>, a collider, body, tile-map collider or notifier
    /// when its <see cref="ScrollFactor"/> is not one or anything in its ancestry is scaled, and a
    /// component that cannot rotate when anything in its ancestry is rotated. Only a
    /// <see cref="Tiles.TileMap"/> whose palette names a layer takes one
    /// <see cref="Tiles.TileMapCollider2D"/>. A refused component stays unattached, as does one whose own
    /// <see cref="Component.OnAttached"/> throws. It is also thrown inside a
    /// <see cref="Rendering.Renderer.Draw"/>.
    /// </exception>
    /// <remarks>
    /// The component takes the next place in attachment order before its <see cref="Component.OnAttached"/>
    /// runs. A component that attaches its parts there steps before them.
    /// </remarks>
    public T Add<T>(T component)
        where T : Component
    {
        ArgumentNullException.ThrowIfNull(component);
        SceneOrNull?.Drawing.ThrowIfDrawing("attached a component");

        if (component.Entity is not null)
        {
            throw new InvalidOperationException(
                $"A {component.GetType().Name} is already attached to a {component.Entity.GetType().Name}. Detach it before attaching it elsewhere.");
        }

        TransformSupport supports = component.Supports;
        if ((supports & TransformSupport.Scroll) == 0 && ScrollFactor != Vector2.One)
        {
            throw Unscrollable(component);
        }

        for (Entity? above = this; above is not null; above = above._parent)
        {
            if ((supports & TransformSupport.Rotation) == 0 && above._local.Rotation != 0f)
            {
                throw Turned(component, this, above, above._local.Rotation);
            }

            if ((supports & TransformSupport.Resize) == 0 && above._local.Scale != Vector2.One)
            {
                throw Scaled(component, this, above, above._local.Scale);
            }
        }

        // Before the hooks, since one may remove this entity and the draw order must know what it now holds.
        NoteRendererChange(component);
        component.Entity = this;
        component.AttachStamp = _components.Count == 0 ? 1 : _components[^1].AttachStamp + 1;
        _components.Add(component);
        if (component.Steps)
        {
            _steppers++;
        }

        bool registered = false;
        try
        {
            component.OnAttachedTo(this);
            registered = true;
            component.OnAttached(this);
        }
        catch
        {
            // The attach leaves no trace. A component that detached itself before throwing is already
            // gone, and one refusing its entity in OnAttachedTo registered nothing.
            if (ReferenceEquals(component.Entity, this))
            {
                if (registered)
                {
                    component.OnDetachingFrom(this);
                }

                Unlist(component);
            }

            throw;
        }

        // OnAttached may have detached the component, which must then not enter the scene. An entity
        // composed by a scene that has not started enters its components when it joins.
        if (Joined && ReferenceEquals(component.Entity, this))
        {
            component.EnterScene();
        }

        // The hooks above may have detached the component or removed this entity. An entity queued to
        // leave, by itself or with an ancestor, still steps until the drain. Its new component waits for
        // the next add before it starts. A scene that has not started starts it as the entity joins.
        if (_started && Joined && SceneOrNull?.Contains(this) == true && ReferenceEquals(component.Entity, this))
        {
            component.RunStart();
        }

        return component;
    }

    /// <summary>Detaches <paramref name="component"/> so it may be attached elsewhere.</summary>
    /// <exception cref="InvalidOperationException">
    /// The component is not attached to this entity, or it is called inside a <see cref="Rendering.Renderer.Draw"/>.
    /// </exception>
    public void Remove(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        SceneOrNull?.Drawing.ThrowIfDrawing("detached a component");

        if (!ReferenceEquals(component.Entity, this))
        {
            throw new InvalidOperationException(
                $"This entity does not hold the {component.GetType().Name} being removed.");
        }

        // Before the hooks run, which then cannot observe the component still attached.
        Unlist(component);

        // Every hook runs and the engine's own release always follows. A throwing hook then leaves no
        // detached component registered or drawn. Failures propagate once detachment finishes.
        List<Exception>? failures = null;
        try
        {
            component.LeaveScene();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            component.OnDetached(this);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        component.OnDetachingFrom(this);
        Scenes.Scene.ThrowCleanupFailures(failures);
    }

    // Takes the component out of this entity's list and clears its owner. A hook may have read the draw order
    // while a renderer was attached, and the order learns it has gone.
    private void Unlist(Component component)
    {
        NoteRendererChange(component);
        _components.RemoveAt(IndexOf(_components, component));
        if (component.Steps)
        {
            _steppers--;
        }

        component.Entity = null;
    }

    // A renderer about to join or leave an entity a scene already holds changes that scene's draw order.
    private void NoteRendererChange(Component component)
    {
        if (component is Renderer renderer)
        {
            renderer.Wake();
            SceneOrNull?.Drawing.Changing(this, band: false);
        }
    }

    /// <summary>Finds the first attached component assignable to <typeparamref name="T"/>.</summary>
    public bool TryGet<T>([NotNullWhen(true)] out T? component)
        where T : Component
    {
        foreach (Component candidate in Components)
        {
            if (candidate is T found)
            {
                component = found;
                return true;
            }
        }

        component = null;
        return false;
    }

    /// <summary>Whether a component assignable to <typeparamref name="T"/> is attached.</summary>
    public bool Has<T>()
        where T : Component =>
        TryGet<T>(out _);

    /// <summary>Gets the first attached component assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No attached component is assignable to that type.</exception>
    public T Get<T>()
        where T : Component =>
        TryGet<T>(out T? component)
            ? component
            : throw new InvalidOperationException(
                $"A {GetType().Name} has no component assignable to {typeof(T).Name}.");

    /// <summary>
    /// Finds the first component assignable to <typeparamref name="T"/> on this entity, then on its parent, then on each
    /// ancestor up to the root.
    /// </summary>
    /// <remarks>The nearest match wins. The search does not visit siblings or <see cref="Children"/>.</remarks>
    public bool TryGetInParent<T>([NotNullWhen(true)] out T? component)
        where T : Component
    {
        for (Entity? entity = this; entity is not null; entity = entity.Parent)
        {
            if (entity.TryGet(out component))
            {
                return true;
            }
        }

        component = null;
        return false;
    }

    /// <summary>
    /// Gets the first component assignable to <typeparamref name="T"/> on this entity, then on its parent, then on each
    /// ancestor up to the root.
    /// </summary>
    /// <remarks>The nearest match wins. The search does not visit siblings or <see cref="Children"/>.</remarks>
    /// <exception cref="InvalidOperationException">No component on this entity or an ancestor is assignable to that type.</exception>
    public T GetInParent<T>()
        where T : Component =>
        TryGetInParent<T>(out T? component)
            ? component
            : throw new InvalidOperationException(
                $"A {GetType().Name} and its ancestors have no component assignable to {typeof(T).Name}.");

    /// <summary>
    /// This entity, its parent or the nearest ancestor assignable to <typeparamref name="T"/>, or null when there is none.
    /// </summary>
    /// <remarks>The search does not visit siblings or <see cref="Children"/>.</remarks>
    public T? FindInParent<T>()
        where T : class
    {
        for (Entity? entity = this; entity is not null; entity = entity.Parent)
        {
            if (entity is T found)
            {
                return found;
            }
        }

        return null;
    }

    // Compares by reference, because a subclass may override Equals and two equal instances are still
    // two separate entities here.
    private static int IndexOf<T>(List<T> items, T item)
        where T : class
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private struct ComponentWalk(List<Component> components)
    {
        private Component? _visited;
        private int _visitedStamp;
        private int _index;

        // Public because foreach only binds to public members. The type itself is private.
        public readonly Component Current => components[_index];

        public readonly ComponentWalk GetEnumerator() => this;

        // Inlined into every step loop, which keeps the walk's fields in registers.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (_visited is not null)
            {
                // The same attachment, not only the same component. One attached again carries a new stamp.
                if (_index < components.Count && ReferenceEquals(components[_index], _visited) &&
                    _visited.AttachStamp == _visitedStamp)
                {
                    _index++;
                }
                else
                {
                    _index = Resume(components, _index, _visitedStamp);
                }
            }

            if (_index >= components.Count)
            {
                return false;
            }

            _visited = components[_index];
            _visitedStamp = _visited.AttachStamp;
            return true;
        }

        // Stamps rise along the list and a detach only removes. When the visited attachment is gone from the
        // cursor, the cursor steps back over every component stamped after it, to the first the walk has not visited.
        // Static and out of line, which keeps the rare recovery out of the inlined walk.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Resume(List<Component> components, int index, int visitedStamp)
        {
            index = Math.Min(index, components.Count);
            while (index > 0 && components[index - 1].AttachStamp > visitedStamp)
            {
                index--;
            }

            return index;
        }
    }
}
