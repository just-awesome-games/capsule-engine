using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class SceneRegistrySource
{
    private const string FileName = "CapsuleScenes.g.cs";

    private const string KeysFileName = "CapsuleAssets.Scenes.g.cs";

    private const string KeysClass = "Scenes";

    private const string DocumentExtension = ".scene.json";

    // Scene and baseScene keys drop this namespace segment, since it repeats the domain.
    private const string DomainSegment = "Scenes";

    // Camera keys drop this namespace segment instead, since it repeats theirs.
    private const string CameraDomainSegment = "Cameras";

    /// <summary>The asset domain a shipped scene document is authored under.</summary>
    internal const string Domain = "scenes";

    // A document's baseScene and camera are resolved against every Scene and Camera subclass the
    // assembly declares, so every one is modeled here whether or not a document ever names it.
    internal static SceneModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        if (!Symbols.DerivesFrom(type, compilation, Symbols.Scene))
        {
            return null;
        }

        Location location = declaration.Identifier.GetLocation();
        string qualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string displayName = type.ToDisplayString();
        bool concreteScene = Symbols.IsConcreteClass(type);
        int contentConstructors = concreteScene
            ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.SceneContent)
            : 0;
        bool parameterless = concreteScene && Symbols.HasPublicParameterlessConstructor(type);
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SceneDocumentAttribute);
        bool accessible = Symbols.IsAccessibleFromGeneratedCode(type);
        int derivableContentConstructors = DerivableConstructorsTaking(type, compilation, Symbols.SceneContent);

        string space = type.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : string.Empty;

        if (annotation is not null)
        {
            if (!concreteScene || contentConstructors == 0)
            {
                return Model(SceneFault.SceneDocumentRequiresContentConstructor);
            }

            if (contentConstructors > 1 || parameterless)
            {
                return Model(SceneFault.AmbiguousConstructors);
            }

            if (annotation.ConstructorArguments.Length != 1)
            {
                return Model(SceneFault.None, registrable: false);
            }

            string documentName = annotation.ConstructorArguments[0].Value as string ?? string.Empty;

            return Model(Accessibility(), documented: true, documentName);
        }

        if (!concreteScene || (contentConstructors == 0 && !parameterless))
        {
            return Model(SceneFault.None, registrable: false);
        }

        if (contentConstructors > 1 || (contentConstructors == 1 && parameterless))
        {
            return Model(SceneFault.AmbiguousConstructors);
        }

        return Model(Accessibility(), documented: contentConstructors == 1);

        SceneFault Accessibility() => accessible ? SceneFault.None : SceneFault.InaccessibleType;

        SceneModel Model(SceneFault fault, bool documented = false, string? declared = null, bool registrable = true) =>
            new(
                qualifiedName, displayName, space, type.Name, documented, declared, fault, registrable,
                type.IsAbstract, derivableContentConstructors, accessible, DeclaredAt.From(location));
    }

    // Every scene the assembly declares, keyed the way a document-backed scene class is. Concrete
    // classes are modeled too, so a baseScene that names one resolves to a class CAP028 can name
    // instead of falling through to CAP030. A class that could actually serve as a base always wins
    // its key over a same-keyed one that never could, whichever declares first, so resolution never
    // depends on declaration order: an eligible pass claims every key it can before an ineligible one
    // is let fill what remains, purely so CAP028 still has a class to name. Two classes that could
    // *both* serve as a baseScene claiming one key is CAP032.
    internal static Dictionary<string, SceneModel> KeyedBaseScenes(
        SourceProductionContext context, ImmutableArray<SceneModel> models, string rootNamespace)
    {
        List<SceneModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));

        Dictionary<string, SceneModel> keyed = new(StringComparer.Ordinal);
        foreach (SceneModel model in ordered)
        {
            if (model.BaseFault != SceneFault.None)
            {
                continue;
            }

            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);
            if (keyed.TryGetValue(key, out SceneModel claimed))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateBaseSceneKey, model.At.Location(), claimed.DisplayName, model.DisplayName, key));
                continue;
            }

            keyed.Add(key, model);
        }

        foreach (SceneModel model in ordered)
        {
            if (model.BaseFault == SceneFault.None)
            {
                continue;
            }

            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);
            if (!keyed.ContainsKey(key))
            {
                keyed.Add(key, model);
            }
        }

        return keyed;
    }

    // A document's "camera" is resolved against every Camera subclass in the assembly, at generation
    // time: the key it claims comes from the same rule a scene document's class does, so no runtime
    // lookup by name is needed. Unlike a scene, no attribute makes the claim explicit, so every class
    // in the hierarchy is modeled, valid or not, and Emit reports the difference between a name that
    // resolves to an unusable class and one no class claims at all.
    internal static CameraModel? DescribeCamera(INamedTypeSymbol type, TypeDeclarationSyntax declaration, Compilation compilation)
    {
        if (!Symbols.DerivesFrom(type, compilation, Symbols.Camera))
        {
            return null;
        }

        string space = type.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : string.Empty;

        return new CameraModel(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            space,
            type.Name,
            Symbols.IsConcreteClass(type),
            Symbols.IsAccessibleFromGeneratedCode(type) && HasAccessibleParameterlessConstructor(type),
            DeclaredAt.From(declaration.Identifier.GetLocation()));
    }

    /// <summary>
    /// Every camera the assembly declares, keyed by declaration order. Two classes claiming one key
    /// is CAP031, the failure a room quietly framed by the wrong camera would otherwise hide.
    /// </summary>
    internal static Dictionary<string, CameraModel> KeyedCameras(
        SourceProductionContext context, ImmutableArray<CameraModel> models, string rootNamespace)
    {
        List<CameraModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));

        Dictionary<string, CameraModel> keyed = new(StringComparer.Ordinal);
        foreach (CameraModel model in ordered)
        {
            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, CameraDomainSegment);
            if (keyed.TryGetValue(key, out CameraModel claimed))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DuplicateCameraKey, model.At.Location(), claimed.DisplayName, model.DisplayName, key));
                continue;
            }

            keyed.Add(key, model);
        }

        return keyed;
    }

    // A parameterless constructor generated code can call: public, internal or protected internal.
    // Generated code sits in the same assembly but does not derive from the type. An internal
    // constructor is reachable from there, and a protected or private protected one is not. The
    // one call site is a camera's own claim. A scene's parameterless shape instead runs through
    // HasPublicParameterlessConstructor, a public API a game can also call.
    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0
                && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
            {
                return true;
            }
        }

        return false;
    }

    // Constructors taking one parameterTypeName that a derived type declared in this assembly can
    // call: every accessibility but private. Counts rather than finds one, so a baseScene candidate
    // faults both zero and more than one the way a registered scene's constructors already do.
    private static int DerivableConstructorsTaking(INamedTypeSymbol type, Compilation compilation, string parameterTypeName)
    {
        INamedTypeSymbol? parameterType = compilation.GetTypeByMetadataName(parameterTypeName);
        if (parameterType is null)
        {
            return 0;
        }

        int count = 0;

        foreach (IMethodSymbol constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Private || constructor.Parameters.Length != 1)
            {
                continue;
            }

            IParameterSymbol parameter = constructor.Parameters[0];
            bool passable = parameter.RefKind is RefKind.None or RefKind.In or RefKind.RefReadOnlyParameter;
            if (passable && SymbolEqualityComparer.Default.Equals(parameter.Type, parameterType))
            {
                count++;
            }
        }

        return count;
    }

    internal static void Emit(
        SourceProductionContext context,
        ImmutableArray<SceneModel> models,
        bool enginePresent,
        string rootNamespace,
        ImmutableArray<SceneDocumentInfo> documents,
        ImmutableArray<CameraModel> cameraModels)
    {
        if (!enginePresent)
        {
            return;
        }

        // A baseScene can name any Scene-deriving class, so KeyedBaseScenes below keys every model.
        // Registration is narrower: only a class Describe found eligible at all.
        ImmutableArray<SceneModel> registrable = [.. models.Where(static model => model.Registrable)];

        List<Registration> sound = [];
        RegistryPass.Sound(
            context,
            registrable,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => Reported(model.Fault),
            model => Resolve(context, sound, model, rootNamespace));

        // Scenes with no document sort first and never collide.
        List<Registration> registered = RegistryPass.Claimed(
            context,
            sound,
            static (left, right) =>
            {
                int byDocument = string.CompareOrdinal(left.DocumentName ?? string.Empty, right.DocumentName ?? string.Empty);

                return byDocument != 0 ? byDocument : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.DocumentName,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.At,
            RegistryDiagnostics.DuplicateSceneDocumentName);

        Dictionary<string, SceneDocumentInfo> documentsByKey = new(StringComparer.Ordinal);
        foreach (SceneDocumentInfo document in documents)
        {
            documentsByKey[document.Key] = document;
        }

        HashSet<string> claimed = new(StringComparer.Ordinal);
        foreach (Registration entry in registered)
        {
            if (entry.DocumentName is not { } name)
            {
                continue;
            }

            claimed.Add(name);

            // A class already composes this document, so a baseScene on it is not a precedence
            // question: the document would name two bases for the same scene.
            if (documentsByKey.TryGetValue(name, out SceneDocumentInfo claimedDocument) && claimedDocument.BaseScene is { } conflicting)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.DocumentBaseSceneConflictsWithAClaim,
                    entry.Model.At.Location(),
                    name,
                    conflicting,
                    entry.Model.DisplayName));
            }
        }

        Dictionary<string, SceneModel> baseScenes = KeyedBaseScenes(context, models, rootNamespace);
        Dictionary<string, CameraModel> cameras = KeyedCameras(context, cameraModels, rootNamespace);

        Dictionary<string, string?> cameraExpressions = new(StringComparer.Ordinal);
        List<UnclaimedDocument> unclaimed = [];
        foreach (SceneDocumentInfo document in documents)
        {
            cameraExpressions[document.Key] = ResolveCamera(context, document, cameras);

            if (!claimed.Contains(document.Key))
            {
                unclaimed.Add(new UnclaimedDocument(document.Key, ResolveBase(context, document, baseScenes)));
            }
        }

        unclaimed.Sort(static (left, right) => string.CompareOrdinal(left.DocumentName, right.DocumentName));

        context.AddSource(FileName, SourceText.From(Render(registered, unclaimed, cameraExpressions), Encoding.UTF8));
        context.AddSource(KeysFileName, SourceText.From(Keys(context, registered, unclaimed), Encoding.UTF8));
    }

    // Every registered document's key as a constant on CapsuleAssets.Scenes, one nested class per
    // key directory. A refused class claim is reported at the class. A shipped document has no file
    // location the generator can see.
    private static string Keys(
        SourceProductionContext context, List<Registration> registered, List<UnclaimedDocument> unclaimed)
    {
        List<KeyValuePair<string, Location>> named = [];
        foreach (Registration entry in registered)
        {
            if (entry.DocumentName is { } name)
            {
                named.Add(new KeyValuePair<string, Location>(name, entry.Model.At.Location()));
            }
        }

        foreach (UnclaimedDocument document in unclaimed)
        {
            named.Add(new KeyValuePair<string, Location>(document.DocumentName, Location.None));
        }

        named.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        RegistryDomain<string> tree = new(
            KeysClass,
            Domain,
            null,
            "scene document",
            "The key of every scene document shipped at <c>assets/" + Domain + "</c>.",
            AppendKey);
        foreach (KeyValuePair<string, Location> key in named)
        {
            string display = Domain + "/" + key.Key + DocumentExtension;
            if (TypeNaming.NormalizeKey(key.Key, out _) is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(RegistryDiagnostics.UnsafeAssetName, key.Value, display));
                continue;
            }

            tree.Add(key.Key, display, key.Key, AssetRegistrySource.Refused<string>(context, key.Value));
        }

        StringBuilder source = RegistryFile.Open();
        tree.Append(source, "        ");

        return RegistryFile.Close(source);
    }

    private static void AppendKey(StringBuilder source, string indent, string identifier, string key)
    {
        source.Append(indent).Append("/// <summary>The scene document <c>").Append(key).AppendLine("</c>.</summary>");
        source.Append(indent).Append("public const string ").Append(identifier)
            .Append(" = ").Append(SymbolDisplay.FormatLiteral(key, quote: true)).AppendLine(";");
    }

    private static string? ResolveCamera(SourceProductionContext context, SceneDocumentInfo document, Dictionary<string, CameraModel> cameras)
    {
        if (document.Camera is not { } key)
        {
            return null;
        }

        if (!cameras.TryGetValue(key, out CameraModel model))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RegistryDiagnostics.UnclaimedSceneKey, Location.None, document.Key, "camera", key));

            return null;
        }

        if (!model.Valid)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RegistryDiagnostics.InvalidCamera, model.At.Location(), model.DisplayName, key));

            return null;
        }

        return "static () => new " + model.QualifiedName + "()";
    }

    private static GeneratedBase? ResolveBase(
        SourceProductionContext context, SceneDocumentInfo document, Dictionary<string, SceneModel> baseScenes)
    {
        if (document.BaseScene is not { } key)
        {
            return null;
        }

        if (!baseScenes.TryGetValue(key, out SceneModel model))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RegistryDiagnostics.UnclaimedSceneKey, Location.None, document.Key, "baseScene", key));

            return null;
        }

        if (model.BaseFault != SceneFault.None)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RegistryDiagnostics.InvalidBaseScene, model.At.Location(), model.DisplayName, key));

            return null;
        }

        return new GeneratedBase(GeneratedSceneClassName(document.Key), model.QualifiedName);
    }

    // The internal sealed scene a document's baseScene generates, named from the document's own key.
    // Each segment's identifier follows a '_', which no identifier contains. Two keys then share a
    // name only when they share every segment's identifier, and the key tree refuses that pair as
    // CAP016. A segment naming no identifier is refused there as CAP017.
    private static string GeneratedSceneClassName(string documentKey)
    {
        StringBuilder name = new("CapsuleGeneratedScene");
        foreach (string part in documentKey.Split('/'))
        {
            name.Append('_').Append(TypeNaming.ToIdentifier(part));
        }

        return name.ToString();
    }

    private static DiagnosticDescriptor? Reported(SceneFault fault) => fault switch
    {
        SceneFault.SceneDocumentRequiresContentConstructor => RegistryDiagnostics.SceneDocumentRequiresContentConstructor,
        SceneFault.InaccessibleType => RegistryDiagnostics.InaccessibleRegisteredType,
        SceneFault.AmbiguousConstructors => RegistryDiagnostics.AmbiguousSceneConstructors,
        _ => null,
    };

    // The key comes from where the type is declared, so it is not settled until the assembly's
    // root namespace is known.
    private static void Resolve(
        SourceProductionContext context,
        List<Registration> sound,
        SceneModel model,
        string rootNamespace)
    {
        if (!model.Documented)
        {
            sound.Add(new Registration(null, model));

            return;
        }

        // An explicit claim is normalized like the document's path, so any spelling of the claim
        // meets the document at the key it ships under.
        string? documentName = model.Declared is { } declared
            ? Normalized(context, model, declared)
            : TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);

        if (documentName is null)
        {
            return;
        }

        if (AssetPaths.IsKey(documentName))
        {
            sound.Add(new Registration(documentName, model));

            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnsafeSceneDocumentName, model.At.Location(), model.DisplayName, documentName));
    }

    private static string? Normalized(SourceProductionContext context, SceneModel model, string declared)
    {
        if (TypeNaming.NormalizeKey(declared, out string? rejected) is { } key)
        {
            return key;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnnameableSceneDocumentSegment, model.At.Location(), model.DisplayName, rejected));

        return null;
    }

    private static string Render(
        List<Registration> registered,
        List<UnclaimedDocument> unclaimed,
        Dictionary<string, string?> cameraExpressions)
    {
        StringBuilder claims = new();
        StringBuilder registrations = new();
        StringBuilder generatedScenes = new();

        foreach (Registration entry in registered)
        {
            registrations.Append("                ");

            if (entry.DocumentName is null)
            {
                registrations.Append("global::Capsule.Scenes.SceneRegistration.Plain(typeof(")
                    .Append(entry.Model.QualifiedName).Append("), static _ => new ")
                    .Append(entry.Model.QualifiedName).AppendLine("()),");
                continue;
            }

            claims.Append("[assembly: global::Capsule.Generated.CapsuleGeneratedRegistryClaimAttribute(1, ")
                .Append(SymbolDisplay.FormatLiteral(entry.DocumentName, quote: true))
                .Append(", typeof(").Append(entry.Model.QualifiedName).AppendLine("))]");

            registrations.Append("global::Capsule.Scenes.SceneRegistration.FromDocument(typeof(")
                .Append(entry.Model.QualifiedName).Append("), ")
                .Append(SymbolDisplay.FormatLiteral(entry.DocumentName, quote: true))
                .Append(", static content => new ").Append(entry.Model.QualifiedName)
                .Append('(').Append(ContentExpression(entry.DocumentName, cameraExpressions)).AppendLine(")),");
        }

        foreach (UnclaimedDocument document in unclaimed)
        {
            string sceneType = "global::Capsule.Scenes.Scene";

            if (document.GeneratedBase is { } generated)
            {
                sceneType = "global::Capsule.Generated." + generated.ClassName;

                // The template a developer no longer writes: the document composes this instead of
                // the Room01.cs a class-only scene would need.
                generatedScenes.Append("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]")
                    .AppendLine()
                    .Append("    internal sealed class ").Append(generated.ClassName)
                    .Append(" : ").AppendLine(generated.QualifiedBase)
                    .AppendLine("    {")
                    .Append("        internal ").Append(generated.ClassName)
                    .AppendLine("(global::Capsule.Scenes.SceneContent content) : base(content)")
                    .AppendLine("        {")
                    .AppendLine("        }")
                    .AppendLine("    }")
                    .AppendLine();
            }

            registrations.Append("                global::Capsule.Scenes.SceneRegistration.DocumentOnly(")
                .Append(SymbolDisplay.FormatLiteral(document.DocumentName, quote: true))
                .Append(", static content => new ").Append(sceneType)
                .Append('(').Append(ContentExpression(document.DocumentName, cameraExpressions)).AppendLine(")),");
        }

        if (claims.Length > 0)
        {
            claims.AppendLine();
        }

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{claims}}namespace Capsule.Generated
            {
            {{generatedScenes}}    /// <summary>Every scene this assembly declares. Generated code. Do not edit.</summary>
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public static class CapsuleScenes
                {
                    internal static global::Capsule.Scenes.SceneRegistration[] Registrations { get; } =
                        new global::Capsule.Scenes.SceneRegistration[]
                        {
            {{registrations}}            };

                    /// <summary>The registry the engine composes every scene through.</summary>
                    public static global::Capsule.Scenes.SceneRegistry Registry { get; } =
                        new global::Capsule.Scenes.SceneRegistry(
                            global::Capsule.Generated.CapsuleEntities.Registry,
                            Registrations);
                }
            }

            """;
    }

    // The content a registration's factory passes on: the document alone, or the document with its
    // resolved camera factory folded in ahead of the constructor that installs it.
    private static string ContentExpression(string documentName, Dictionary<string, string?> cameraExpressions) =>
        cameraExpressions.TryGetValue(documentName, out string? camera) && camera is not null
            ? "content!.Value with { Camera = " + camera + " }"
            : "content!.Value";

    private readonly struct UnclaimedDocument(string documentName, GeneratedBase? generatedBase)
    {
        internal string DocumentName { get; } = documentName;

        internal GeneratedBase? GeneratedBase { get; } = generatedBase;
    }

    private readonly struct GeneratedBase(string className, string qualifiedBase)
    {
        internal string ClassName { get; } = className;

        internal string QualifiedBase { get; } = qualifiedBase;
    }

    private readonly struct Registration(string? documentName, SceneModel model)
    {
        internal string? DocumentName { get; } = documentName;

        internal SceneModel Model { get; } = model;
    }
}
