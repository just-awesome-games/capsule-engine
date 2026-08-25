namespace Capsule.Tests.Levels;

/// <summary>
/// The specs that drive an importer through a <see cref="LevelFixtures.Workspace"/>. A
/// workspace owns the process working directory, and that is not something two collections can
/// hold at the same time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LevelWorkspaceCollection
{
    internal const string Name = "level-workspace";
}
