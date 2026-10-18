namespace Capsule.Scenes;

public partial class Scene
{
    /// <summary>The entities of a scene assignable to <typeparamref name="T"/>, walked by <see langword="foreach"/>.</summary>
    /// <typeparam name="T">The class or interface the walk yields.</typeparam>
    public struct EntityWalk<T>
        where T : class
    {
        private readonly Scene _scene;
        private readonly int _version;
        private int _index;

        internal EntityWalk(Scene scene)
        {
            _scene = scene;
            _version = scene._tree.Version;
            _index = -1;
        }

        /// <summary>The entity the walk stands on.</summary>
        public readonly T Current => (T)(object)_scene._tree.Slots[_index];

        /// <summary>The walk itself, which <see langword="foreach"/> binds to.</summary>
        public readonly EntityWalk<T> GetEnumerator() => this;

        /// <summary>Steps to the next entity assignable to <typeparamref name="T"/>, or returns false past the last.</summary>
        /// <exception cref="InvalidOperationException">
        /// An entity was added outside a step since the walk began, or removed outside a step and then compacted away by a query.
        /// </exception>
        public bool MoveNext()
        {
            if (_version != _scene._tree.Version)
            {
                throw new InvalidOperationException(
                    $"A {_scene.GetType().Name}'s entities moved during a walk over them, which would skip or repeat one. " +
                    "Collect the entities to add or remove, and change the scene after the walk.");
            }

            List<Entity> entities = _scene._tree.Slots;
            while (++_index < entities.Count)
            {
                if (entities[_index] is T)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
