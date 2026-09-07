using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal readonly struct InputDriverModel : IEquatable<InputDriverModel>
{
    internal InputDriverModel(string qualifiedName, string displayName, string typeName, bool accessible, Location location)
    {
        QualifiedName = qualifiedName;
        DisplayName = displayName;
        TypeName = typeName;
        Accessible = accessible;
        Location = location;
    }

    internal string QualifiedName { get; }

    internal string DisplayName { get; }

    /// <summary>The simple class name, which is the key <c>--driver</c> takes.</summary>
    internal string TypeName { get; }

    internal bool Accessible { get; }

    internal Location Location { get; }

    // Location participates in equality only for a faulted model, so an unrelated edit does not
    // re-emit the registry.
    public bool Equals(InputDriverModel other) =>
        Accessible == other.Accessible
        && string.Equals(QualifiedName, other.QualifiedName, StringComparison.Ordinal)
        && string.Equals(TypeName, other.TypeName, StringComparison.Ordinal)
        && (Accessible || Location.Equals(other.Location));

    public override bool Equals(object? obj) => obj is InputDriverModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + QualifiedName.GetHashCode();
        hash = (hash * 31) + TypeName.GetHashCode();
        hash = (hash * 31) + (Accessible ? 1 : 0);

        return hash;
    }
}

internal static class InputDriverRegistrySource
{
    private const string FileName = "CapsuleGameInputDrivers.g.cs";

    internal static InputDriverModel? Describe(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type)
        {
            return null;
        }

        // A driver the command line names is constructed by the generated registry, so one that
        // takes arguments registers under no name; it reaches a run through WithInputDriver.
        if (!Symbols.IsConcreteClass(type)
            || !Symbols.HasPublicParameterlessConstructor(type)
            || !Symbols.Implements(type, context.SemanticModel.Compilation, Symbols.InputDriver))
        {
            return null;
        }

        return new InputDriverModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            type.Name,
            Symbols.IsAccessibleFromGeneratedCode(type),
            declaration.Identifier.GetLocation());
    }

    internal static void Emit(SourceProductionContext context, ImmutableArray<InputDriverModel> models, bool registered)
    {
        if (registered)
        {
            context.AddSource(FileName, SourceText.From(Render(Sound(context, models)), Encoding.UTF8));
        }
    }

    // Every driver the assembly registers, in a stable order: one entry per class, each accessible
    // to generated code and each claiming a name no other class in the assembly claims.
    internal static List<InputDriverModel> Sound(SourceProductionContext context, ImmutableArray<InputDriverModel> models)
    {
        List<InputDriverModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.Location, right.QualifiedName, right.Location));

        List<InputDriverModel> sound = new(ordered.Count);
        HashSet<string> described = new(StringComparer.Ordinal);
        Dictionary<string, InputDriverModel> claimed = new(StringComparer.Ordinal);
        foreach (InputDriverModel model in ordered)
        {
            // The parts of a partial class are one type.
            if (!described.Add(model.QualifiedName))
            {
                continue;
            }

            if (!model.Accessible)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.InaccessibleRegisteredType, model.Location, model.DisplayName));
                continue;
            }

            if (claimed.TryGetValue(model.TypeName, out InputDriverModel previous))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateInputDriverName,
                    model.Location,
                    previous.DisplayName,
                    model.DisplayName,
                    model.TypeName));
                continue;
            }

            claimed.Add(model.TypeName, model);
            sound.Add(model);
        }

        return sound;
    }

    // One registration's construction, which reads the same inline in the shell's entry point as it
    // does in a logic assembly's registry.
    internal static void AppendRegistration(StringBuilder source, InputDriverModel model)
    {
        source.Append("new global::Capsule.Scenes.Input.InputDriverRegistration(");
        source.Append(SymbolDisplay.FormatLiteral(model.TypeName, quote: true));
        source.Append(", static () => new ");
        source.Append(model.QualifiedName);
        source.Append("())");
    }

    private static string Render(List<InputDriverModel> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Scenes.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every input driver this assembly declares. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameInputDrivers");
        source.AppendLine("    {");
        source.AppendLine("        internal static global::Capsule.Scenes.Input.InputDriverRegistration[] Registrations { get; } =");
        source.AppendLine("            new global::Capsule.Scenes.Input.InputDriverRegistration[]");
        source.AppendLine("            {");

        foreach (InputDriverModel model in registered)
        {
            source.Append("                ");
            AppendRegistration(source, model);
            source.AppendLine(",");
        }

        source.AppendLine("            };");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
