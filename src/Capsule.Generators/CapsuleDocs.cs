namespace Capsule.Generators;

// The documentation pages diagnostic help links point at.
internal static class CapsuleDocs
{
    internal const string LogicBoundary = "architecture.md#logic-boundary";
    internal const string NamedAssets = "assets.md#named-assets";
    internal const string Scenes = "scenes.md";
    internal const string Fonts = "assets.md#fonts";

    internal static string At(string page) =>
        "https://github.com/just-awesome-games/capsule-engine/blob/main/docs/" + page;
}
