namespace Capsule.Runtime;

/// <summary>
/// The engine's entry point, carrying nothing a game generates. A game's <c>Program</c> starts at
/// the <c>Capsule.Runtime.Generated.GameBoot</c> generated into its shell, which starts here.
/// </summary>
public static class CapsuleEngine
{
    public static EngineBuilder Configure() => new();
}
