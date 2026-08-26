namespace Capsule.Tests.Maps;

/// <summary>
/// The specs that drive an importer through a <see cref="MapFixtures.Workspace"/>. A workspace
/// owns the process working directory, and that is not something two collections can hold at
/// the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MapWorkspaceCollection
{
    internal const string Name = "map-workspace";
}
