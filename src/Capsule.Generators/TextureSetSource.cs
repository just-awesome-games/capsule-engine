using System.Collections.Immutable;
using System.Text;

namespace Capsule.Generators;

/// <summary>
/// How a derived residency set reaches the registry that carries it: one method per registration
/// that has groups, adding each group's handles to the set being assembled.
/// </summary>
internal static class TextureSetSource
{
    private const string MethodPrefix = "Textures";

    private const string SetType = "global::System.Collections.Generic.List<global::Capsule.Assets.TextureHandle>";

    /// <summary>The argument a registration passes, or nothing when its class reaches no group.</summary>
    internal static string ArgumentFor(ImmutableArray<string> groups, int index) =>
        groups.IsDefaultOrEmpty ? string.Empty : ", " + MethodPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Emits a builder for every registration that has groups, in registration order.</summary>
    internal static void AppendBuilders(StringBuilder source, List<ImmutableArray<string>> sets, string indent)
    {
        for (int i = 0; i < sets.Count; i++)
        {
            if (sets[i].IsDefaultOrEmpty)
            {
                continue;
            }

            source.Append(indent).Append("private static void ").Append(MethodPrefix)
                .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('(').Append(SetType).AppendLine(" set)");
            source.Append(indent).AppendLine("{");

            foreach (string group in sets[i])
            {
                source.Append(indent).Append("    set.AddRange(").Append(TextureResidency.ReferenceTo(group)).AppendLine(");");
            }

            source.Append(indent).AppendLine("}");
            source.AppendLine();
        }
    }
}
