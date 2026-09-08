using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>The scene document and entity registry a <see cref="Scene"/> is composed from.</summary>
/// <param name="Document">The document to compose.</param>
/// <param name="Entities">What each spawn type constructs.</param>
public readonly record struct SceneContent(SceneDocument Document, EntityRegistry Entities);
