using System.Text;
using Capsule.Build.Registry;
using Capsule.Scenes.Documents;

namespace Capsule.Build.Scenes;

/// <summary>
/// Writes a scene document's member: a <c>SceneKey</c> constant carrying the attribute the generator
/// composes the scene registry from.
/// </summary>
internal static class SceneMembers
{
    internal static void Write(StringBuilder code, string indent, string identifier, Source document, string[] attributes)
    {
        code.Append(indent).Append("/// <summary>The scene document <c>").Append(document.Key).AppendLine("</c>.</summary>");
        foreach (string attribute in attributes)
        {
            code.Append(indent).Append('[').Append(attribute).AppendLine("]");
        }

        code.Append(indent).Append("public static ").Append(GeneratedTypes.SceneKey).Append(' ').Append(identifier)
            .Append(" => new ").Append(GeneratedTypes.SceneKey).Append('(').Append(Literal.Of(document.Key)).AppendLine(");");
    }

    /// <summary>The attribute marking one document's key member: its key, and the baseScene it names.</summary>
    internal static string[] Attributes(SceneDocument scene, string key)
    {
        List<string> named = [$"Key = {Literal.Of(key)}"];
        if (scene.BaseScene is { } baseScene)
        {
            named.Add($"BaseScene = {Literal.Of(baseScene)}");
        }

        return [$"{GeneratedAttributes.SceneDocumentName}({string.Join(", ", named)})"];
    }
}
