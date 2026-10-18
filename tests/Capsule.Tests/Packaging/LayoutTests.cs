using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Tests.Packaging;

// How the repo lays out its code: member order in every type, and the size of a test file.
public sealed class LayoutTests
{
    [Fact]
    public void NoType_DeclaresStateAfterAConstructor()
    {
        string root = ProseTests.RepositoryRoot();
        List<string> broken = [];

        foreach (string path in new[] { "src", "samples", "tests" }.SelectMany(folder => ProseTests.Files(Path.Combine(root, folder))))
        {
            if (!path.EndsWith(".cs", StringComparison.Ordinal) || ProseTests.Vendored(path, root))
            {
                continue;
            }

            CompilationUnitSyntax unit = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
            foreach (TypeDeclarationSyntax type in unit.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                bool constructed = false;
                foreach (MemberDeclarationSyntax member in type.Members)
                {
                    constructed |= member is ConstructorDeclarationSyntax;
                    if (constructed && IsState(member))
                    {
                        int line = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        broken.Add($"{Path.GetRelativePath(root, path)}:{line}");
                    }
                }
            }
        }

        Assert.True(broken.Count == 0, $"A type declares its state first: constants, static fields, fields and state-holding properties, then its constructors, then every other member. These members hold state but follow a constructor. Move each above the type's first constructor:{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    [Fact]
    public void NoTestFile_RunsPast400Lines()
    {
        string root = ProseTests.RepositoryRoot();
        string[] suites = [Path.Combine(root, "tests", "Capsule.Tests"), Path.Combine(root, "samples", "MinimalGame", "tests")];
        List<string> broken =
        [
            .. suites.SelectMany(ProseTests.Files)
                .Where(static path => path.EndsWith(".cs", StringComparison.Ordinal) && File.ReadLines(path).Count() > 400)
                .Select(path => Path.GetRelativePath(root, path)),
        ];

        Assert.True(broken.Count == 0, $"A test file stays under 400 lines. Split each of these by subject:{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    // A field, an event field, an auto-property, or a property whose accessors use its backing field.
    private static bool IsState(MemberDeclarationSyntax member) => member switch
    {
        BaseFieldDeclarationSyntax => true,
        PropertyDeclarationSyntax property =>
            (property.AccessorList is { } accessors
                && accessors.Accessors.All(static accessor => accessor.Body is null && accessor.ExpressionBody is null))
            || ((SyntaxNode?)property.AccessorList ?? property.ExpressionBody)?.DescendantTokens().Any(static token => token.Text == "field") == true,
        _ => false,
    };
}
