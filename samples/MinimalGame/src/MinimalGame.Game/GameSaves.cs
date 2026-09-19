using System.Text.Json.Serialization;
using Capsule.Persistence;

namespace MinimalGame.Game;

/// <summary>What the player keeps between runs. One document, written when the title menu changes it.</summary>
public sealed record GameSettings
{
    /// <summary>Whether sound effects are audible; the <c>sfx</c> bus is levelled from it at boot.</summary>
    public bool SoundOn { get; init; } = true;
}

/// <summary>The game's save documents, declared once beside the rest of its declarations.</summary>
public static class GameSaves
{
    /// <summary>The settings document. A first run has none, so it reads as the default settings.</summary>
    public static readonly SaveKey<GameSettings> Settings =
        new("settings", GameSaveContext.Default.GameSettings, new GameSettings());
}

// Serialization is generated, because a game ships under NativeAOT and reflection-based
// serialization is off.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GameSettings))]
internal sealed partial class GameSaveContext : JsonSerializerContext;
