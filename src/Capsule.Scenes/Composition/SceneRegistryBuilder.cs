using System.ComponentModel;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>Gathers what each logic assembly contributes into one <see cref="SceneRegistry"/>.</summary>
/// <remarks>
/// A shell's <c>CapsuleBoot</c> and a logic assembly's <c>CapsuleScenes.Registry</c> both build through it. A test
/// composing through <c>CapsuleScenes.Registry</c> then composes what a run does.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SceneRegistryBuilder
{
    private readonly List<EntityRegistration> _entities = [];
    private readonly List<SceneRegistration> _scenes = [];
    private readonly List<TileTypeComposer> _tileTypes = [];

    /// <summary>Adds one assembly's entity registrations.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddEntities(IEnumerable<EntityRegistration> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entities.AddRange(entities);
    }

    /// <summary>Adds one assembly's scene registrations.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddScenes(IEnumerable<SceneRegistration> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        _scenes.AddRange(scenes);
    }

    /// <summary>Adds one assembly's tile type composer. A null composer, from an assembly declaring no tile type, adds nothing.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddTileTypes(TileTypeComposer? tileTypes)
    {
        if (tileTypes is not null)
        {
            _tileTypes.Add(tileTypes);
        }
    }

    /// <summary>The registry over everything added.</summary>
    /// <exception cref="ArgumentException">Two assemblies register the same spawn type, scene class or scene document.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SceneRegistry Build() => new(new EntityRegistry(_entities), _scenes, ComposeTileType());

    // A palette entry's type is claimed by at most one assembly, so the first composer returning a tile builds it.
    private TileTypeComposer? ComposeTileType()
    {
        switch (_tileTypes.Count)
        {
            case 0:
                return null;
            case 1:
                return _tileTypes[0];
        }

        TileTypeComposer[] composers = [.. _tileTypes];

        return (type, tile, properties) =>
        {
            foreach (TileTypeComposer compose in composers)
            {
                if (compose(type, tile, properties) is { } composed)
                {
                    return composed;
                }
            }

            return null;
        };
    }
}
