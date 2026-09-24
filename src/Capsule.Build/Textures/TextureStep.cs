using Capsule.Build.Registry;

namespace Capsule.Build.Textures;

/// <summary>
/// Every texture, shipped at its path and declared as a <c>TextureHandle</c>. One an atlas packed
/// ships on that atlas's page instead.
/// </summary>
internal static class TextureStep
{
    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    /// <param name="packed">The texture keys an atlas packed.</param>
    internal static void Build(BuildPass pass, HashSet<string> packed)
    {
        foreach ((Source texture, _) in pass.Each(
            pass.Of(AssetType.Textures),
            source =>
            {
                if (!packed.Contains(source.Key))
                {
                    pass.Shipped.Copy(source.Path, source.Key + source.Extension);
                }

                return source;
            }))
        {
            pass.Declare(texture, (source, indent, identifier) =>
            {
                source.Append(indent).Append("/// <summary><c>").Append(texture.Key).Append(texture.Extension).AppendLine("</c>.</summary>");
                source.Append(indent).Append("public static ").Append(HandleType).Append(' ').Append(identifier)
                    .Append(" => new ").Append(HandleType).Append('(').Append(Literal.Of(texture.Key)).Append(", ")
                    .Append(Literal.Of(texture.Extension)).AppendLine(");");
            });
        }
    }
}
