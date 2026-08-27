using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Scenes.Generator;

/// <summary>
/// The <c>GameAssets</c> registry: every asset the build ships, as a typed handle nested under its
/// domain. The domain each asset belongs to arrives as item metadata on the additional file, so
/// this decides nothing about where an asset may live — <c>build/Capsule.Assets.targets</c> does,
/// and the table below is the other half of that agreement.
/// </summary>
internal static class AssetRegistrySource
{
    private const string FileName = "CapsuleGameAssets.g.cs";
    private const string DomainMetadata = "build_metadata.AdditionalFiles.CapsuleAssetDomain";

    private static readonly (string Domain, string ClassName, string Handle)[] Domains =
    [
        ("textures", "Textures", "global::Capsule.Assets.TextureHandle"),
        ("audio", "Audio", "global::Capsule.Assets.AudioHandle"),
        ("fonts", "Fonts", "global::Capsule.Assets.FontHandle"),
    ];

    internal static AssetModel? Describe(AdditionalText text, AnalyzerConfigOptionsProvider options)
    {
        // Every other additional file a project carries — an .editorconfig-adjacent list, a
        // consumer's own analyzer input — reaches this the same way and is not an asset.
        if (!options.GetOptions(text).TryGetValue(DomainMetadata, out string? domain)
            || string.IsNullOrEmpty(domain))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(text.Path);
        string? identifier = TypeNaming.ToIdentifier(name);

        return new AssetModel(
            domain,
            name,
            Path.GetFileName(text.Path),
            identifier ?? string.Empty,
            FaultIn(domain, identifier));
    }

    internal static void Emit(SourceProductionContext context, ImmutableArray<AssetModel> models, bool emitting)
    {
        if (!emitting)
        {
            return;
        }

        List<AssetModel> sound = new(models.Length);
        foreach (AssetModel model in models)
        {
            if (model.Fault == AssetFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, Location.None, model.FileName));
                continue;
            }

            if (model.Fault == AssetFault.DomainCollision)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.AssetNamedAfterItsDomain, Location.None, model.FileName, model.Identifier));
                continue;
            }

            sound.Add(model);
        }

        // Sorted before the duplicate check reads off it, and before anything is rendered: the
        // additional files arrive in whatever order MSBuild collected them, and generated source
        // that reorders itself between machines is a diff nobody made.
        sound.Sort(static (left, right) =>
        {
            int byDomain = string.CompareOrdinal(left.Domain, right.Domain);
            if (byDomain != 0)
            {
                return byDomain;
            }

            int byIdentifier = string.CompareOrdinal(left.Identifier, right.Identifier);

            return byIdentifier != 0 ? byIdentifier : string.CompareOrdinal(left.FileName, right.FileName);
        });

        List<AssetModel> registered = new(sound.Count);
        foreach (AssetModel model in sound)
        {
            // Two names the build kept apart can still meet here: the shipped tree separates
            // 'a-b' from 'a_b', and one identifier does not.
            if (registered.Count > 0
                && string.Equals(registered[registered.Count - 1].Domain, model.Domain, StringComparison.Ordinal)
                && string.Equals(registered[registered.Count - 1].Identifier, model.Identifier, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateAssetIdentifier,
                    Location.None,
                    registered[registered.Count - 1].FileName,
                    model.FileName,
                    model.Identifier,
                    model.Domain));
                continue;
            }

            registered.Add(model);
        }

        context.AddSource(FileName, SourceText.From(Render(registered), Encoding.UTF8));
    }

    // A member may not carry the name of the class it is declared on (CS0542), and a domain's
    // class is the one every asset in it lands on — so 'audio/audio.wav' is a name no registry
    // can hold, whatever else is authored beside it.
    private static AssetFault FaultIn(string domain, string? identifier)
    {
        if (identifier is null)
        {
            return AssetFault.UnsafeName;
        }

        for (int i = 0; i < Domains.Length; i++)
        {
            if (string.Equals(Domains[i].Domain, domain, StringComparison.Ordinal))
            {
                return string.Equals(Domains[i].ClassName, identifier, StringComparison.Ordinal)
                    ? AssetFault.DomainCollision
                    : AssetFault.None;
            }
        }

        return AssetFault.None;
    }

    private static string Render(List<AssetModel> registered)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Assets.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every asset this game ships, as typed handles. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameAssets");
        source.AppendLine("    {");

        // Every domain is emitted whatever the game authored, so a call site naming one always
        // compiles and only the asset it names can be missing.
        for (int i = 0; i < Domains.Length; i++)
        {
            if (i > 0)
            {
                source.AppendLine();
            }

            AppendDomain(source, registered, Domains[i]);
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    private static void AppendDomain(
        StringBuilder source,
        List<AssetModel> registered,
        (string Domain, string ClassName, string Handle) domain)
    {
        source.Append("        /// <summary>Everything shipped at <c>Assets/").Append(domain.Domain).AppendLine("</c>.</summary>");
        source.Append("        public static class ").AppendLine(domain.ClassName);
        source.AppendLine("        {");

        foreach (AssetModel model in registered)
        {
            if (!string.Equals(model.Domain, domain.Domain, StringComparison.Ordinal))
            {
                continue;
            }

            source.Append("            /// <summary><c>").Append(model.FileName).AppendLine("</c>.</summary>");
            source.Append("            public static ").Append(domain.Handle).Append(' ').Append(model.Identifier);
            source.Append(" => new ").Append(domain.Handle).Append('(');
            source.Append(SymbolDisplay.FormatLiteral(model.Name, quote: true));
            source.AppendLine(");");
        }

        source.AppendLine("        }");
    }
}
