namespace Capsule.Generators;

// Where a diagnostic's help link points: the page of the engine's own documentation that states the
// rule the diagnostic enforces.
internal static class CapsuleDocs
{
    internal const string LogicBoundary = "architecture.md#logic-boundary";
    internal const string NamedAssets = "consuming-capsule.md#named-assets";
    internal const string Scenes = "scenes.md";
    internal const string Sheets = "sprite-animation.md";
    internal const string Fonts = "text.md";

    internal static string At(string page) =>
        "https://github.com/just-awesome-games/capsule-engine/blob/main/docs/" + page;
}
