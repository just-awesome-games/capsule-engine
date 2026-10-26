using System.Reflection;
using System.Xml.Linq;
using Capsule.Assets;
using Capsule.Build;
using Capsule.Physics;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Packaging;

public sealed class ApiReferenceTests
{
    public static TheoryData<string> ShippedAssemblies { get; } = new(
        typeof(AssetCollection).Assembly.GetName().Name!,
        typeof(CapsuleBuild).Assembly.GetName().Name!,
        typeof(Aabb2D).Assembly.GetName().Name!,
        typeof(Scene).Assembly.GetName().Name!,
        typeof(CapsuleEngine).Assembly.GetName().Name!);

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheShippedDocumentation_ListsOnlyThePublicSurface(string assemblyName)
    {
        // Internal symbols are imported, and each identifier resolves through the compiler to the one overload it names.
        Compilation compilation = CSharpCompilation.Create(
            "ApiReference",
            references: GeneratorHarness.Referenced(null, Assembly.Load(assemblyName)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithMetadataImportOptions(MetadataImportOptions.All));
        List<string> hidden = [];

        foreach (string identifier in DocumentedMembers(assemblyName))
        {
            if (DocumentationCommentId.GetFirstSymbolForDeclarationId(identifier, compilation) is not { } symbol)
            {
                // An identifier the compiler cannot resolve is reported, never assumed public.
                hidden.Add($"{identifier} (unresolved)");
            }
            else if (!IsReachable(symbol))
            {
                hidden.Add(identifier);
            }
        }

        // The identifier is the whole message, and a collection assertion elides a long one.
        Assert.True(hidden.Count == 0, $"{assemblyName}.xml ships documentation a consumer cannot reach:{Environment.NewLine}{string.Join(Environment.NewLine, hidden)}");
    }

    // A null or out-of-range argument throws by the .NET convention, which no member restates.
    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheShippedDocumentation_DocumentsNoArgumentValidation(string assemblyName)
    {
        List<string> documented = [.. Documentation(assemblyName)
            .Descendants("member")
            .Where(member => member.Elements("exception").Any(exception =>
                (string?)exception.Attribute("cref") is "T:System.ArgumentNullException" or "T:System.ArgumentOutOfRangeException"))
            .Select(member => (string?)member.Attribute("name") ?? "(unnamed)")];

        Assert.True(documented.Count == 0, $"{assemblyName}.xml documents argument validation. Delete the <exception> tag from each:{Environment.NewLine}{string.Join(Environment.NewLine, documented)}");
    }

    private static XDocument Documentation(string assemblyName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".xml");
        Assert.True(File.Exists(path), $"{assemblyName} ships no API reference beside its assembly.");

        return XDocument.Load(path);
    }

    private static IEnumerable<string> DocumentedMembers(string assemblyName) =>
        Documentation(assemblyName)
            .Descendants("member")
            .Select(member => (string?)member.Attribute("name"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();

    // A type is reachable when it and every type around it are public. A member is reachable when its
    // declaring type is and it is public or protected. A property or event takes its most visible accessor.
    private static bool IsReachable(ISymbol symbol) =>
        (symbol is ITypeSymbol
            ? symbol.DeclaredAccessibility is Accessibility.Public
            : symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
        && (symbol.ContainingType is not { } outer || IsReachable(outer));
}
