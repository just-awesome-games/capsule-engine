using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Generators;

// How a name or a value is spelled in generated C#.
internal static class CodeText
{
    /// <summary>The text as a quoted C# string literal.</summary>
    internal static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    /// <summary>The name as an identifier, escaped with <c>@</c> when it is a keyword.</summary>
    internal static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>The text with every character but a letter or digit replaced by an underscore: <c>Game.Door</c> is <c>Game_Door</c>.</summary>
    internal static string Underscored(string text) =>
        new(text.Select(static character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    /// <summary>
    /// A fully qualified type name as one identifier, distinct for each name: <c>global::Game.Door</c> is
    /// <c>Game_Door</c>, and <c>global::Game_Door</c> is <c>Game_0Door</c>.
    /// </summary>
    /// <remarks>
    /// A dot is an underscore, and an underscore is <c>_0</c>, which no dot can make since no name starts with a digit.
    /// A keyword's escaping <c>@</c> is dropped.
    /// </remarks>
    internal static string TypeIdentifier(string qualifiedName)
    {
        StringBuilder identifier = new();
        foreach (char character in qualifiedName.Substring("global::".Length))
        {
            _ = character switch
            {
                '_' => identifier.Append("_0"),
                '@' => identifier,
                _ => identifier.Append(char.IsLetterOrDigit(character) ? character : '_'),
            };
        }

        return identifier.ToString();
    }
}
