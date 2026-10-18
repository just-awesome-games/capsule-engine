using System.Numerics;
using Capsule.Assets;
using Capsule.Tiles;

namespace Capsule.Scenes;

// Composes a scene document into a scene: one entity per entry in authored order, then the scene's own members.
internal static class SceneComposition
{
    // Adds every entry's entity to scene and applies the document's members. Returns every texture and
    // sound the placements author, which the preload collects.
    internal static AssetCollection Compose(Scene scene, SceneContent content)
    {
        ArgumentNullException.ThrowIfNull(content.Document);
        ArgumentNullException.ThrowIfNull(content.Entities);

        // References are set once every entry is constructed. A reference may name a later entry.
        ReadOnlySpan<SceneDocumentEntry> entries = content.Document.Entries;
        Dictionary<int, Entity> placed = [];
        AuthoredMembers[] spawned = new AuthoredMembers[entries.Length];
        AssetCollection assets = new();
        for (int i = 0; i < entries.Length; i++)
        {
            SceneDocumentEntry entry = entries[i];
            AuthoredMembers authored = new(entry, i, placed, assets);
            Entity entity;
            try
            {
                entity = content.Entities.Create(entry.Spawn with { Type = entry.Type }, authored);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw authored.Refused(ex);
            }

            scene.Add(entity);
            spawned[i] = authored;
            if (entry.Id is { } id)
            {
                placed.Add(id, entity);
            }

            // The size the scene authors, applied below, overrides the one its maps span.
            if (entity is TileMap map)
            {
                scene.Size = Vector2.Max(scene.Size, map.Size);
            }
        }

        foreach (AuthoredMembers authored in spawned)
        {
            authored.Finish();
        }

        // The scene's own members land once every entry is built, before a subclass constructor body runs.
        AuthoredMembers members = new(content.Document, placed, assets);
        content.Apply?.Invoke(scene, members);
        members.Finish();
        return assets;
    }
}
