using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// The wiring from what the compilation holds to each file the generator writes and each check it runs.
// Every step's logic lives in its domain's folder. A plan carries its diagnostics as data. Diagnostic
// compares its arguments by reference, so a plan holding any is never cached and its step reports again.
[Generator(LanguageNames.CSharp)]
public sealed class RegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // What the project is.
        IncrementalValueProvider<GeneratorRole> role = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => GeneratorRoles.Read(options.GlobalOptions));
        IncrementalValueProvider<ProjectConfiguration> project = context.CompilationProvider
            .Combine(role)
            .Select(static (input, _) =>
            {
                (Compilation compilation, GeneratorRole declared) = input;

                return new ProjectConfiguration(
                    declared,
                    ScenesReferenced: compilation.GetTypeByMetadataName(MetadataNames.Scene) is not null,
                    RuntimeReferenced: compilation.GetTypeByMetadataName(MetadataNames.CapsuleEngine) is not null,
                    compilation.AssemblyName ?? "Game");
            });
        IncrementalValueProvider<bool> isLogicAssembly = project.Select(static (configured, _) => configured.IsLogicAssembly);
        IncrementalValueProvider<string?> providerName = project
            .Select(static (configured, _) => configured.IsLogicAssembly ? TypeNaming.RegistryProviderName(configured.AssemblyName) : null);
        IncrementalValueProvider<string> rootNamespace = RootNamespace(context);

        // What the game declares. Syntax-backed models come from one pass over every type declaration.
        IncrementalValuesProvider<RegistryCandidate> candidates = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => MayBeRegistered(node), static (syntax, cancellation) => Describe(syntax, cancellation))
            .Where(static candidate => !candidate.IsEmpty);
        IncrementalValueProvider<EquatableArray<EntityModel>> entities = Collected(candidates, static candidate => candidate.Entity);
        IncrementalValueProvider<EquatableArray<SceneModel>> scenes = Collected(candidates, static candidate => candidate.Scene);
        IncrementalValueProvider<EquatableArray<InputDriverModel>> drivers = Collected(candidates, static candidate => candidate.Driver);

        // The engine's own Scene, whose members every document's top-level keys may set, and its tile map, which
        // every document may place.
        IncrementalValueProvider<SceneModel?> engineScene = context.CompilationProvider
            .Select(static (compilation, _) => SceneDescriber.DescribeEngineScene(compilation));
        IncrementalValueProvider<EntityModel?> engineTileMap = context.CompilationProvider
            .Select(static (compilation, _) => EntityDescriber.DescribeEngine(compilation, MetadataNames.TileMap));

        // What the build declares on CapsuleAssets: every scene document it shipped, whether or not a class
        // claims it, with the baseScene it names, read once by the build's own parser. And every
        // texture and sound, which a placement's asset member names by key.
        IncrementalValueProvider<EquatableArray<SceneDocumentModel>> documents = Collected(Marked(
            context, MetadataNames.SceneDocumentKeyAttribute, static marked => SceneDescriber.DescribeDocument(marked)));
        IncrementalValueProvider<EquatableArray<AssetModel>> assets = Collected(Marked(
            context, MetadataNames.AssetAttribute, static marked => AssetTable.Describe(marked)));

        // What each [Authorable] member is, checked where it is declared, whether or not a class spawns it.
        IncrementalValuesProvider<AuthorableFault> authorableFaults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MetadataNames.AuthorableAttribute,
                static (node, _) => node is PropertyDeclarationSyntax or VariableDeclaratorSyntax,
                static (marked, _) => AuthorableCheck.Describe(marked))
            .Where(static fault => fault is not null)
            .Select(static (fault, _) => fault!.Value);
        IncrementalValueProvider<EquatableArray<string>> writableAuthorableFields = Collected(context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MetadataNames.AuthorableAttribute,
                static (node, _) => node is VariableDeclaratorSyntax,
                static (marked, _) => AuthorableSuppressionRenderer.Describe(marked))
            .Where(static id => id is not null)
            .Select(static (id, _) => id!));

        // What the shell references. Only the shell walks every referenced assembly's attributes, so the
        // role filter comes before the walk.
        IncrementalValueProvider<BootModel> boot = role
            .Combine(context.CompilationProvider)
            .Select(static (input, _) => input.Left == GeneratorRole.Shell ? BootDescriber.Describe(input.Right) : BootModel.None)
            .WithTrackingName("BootModel");

        // The plans each file is rendered from.
        IncrementalValueProvider<EntityPlan> entityPlan = entities
            .Combine(engineTileMap).Combine(isLogicAssembly).Combine(rootNamespace).Combine(documents).Combine(assets)
            .Select(static (input, _) =>
            {
                var (((((models, engine), logic), root), shipped), declared) = input;

                return EntityResolver.Resolve(new EntityInputs(models, engine, logic, root, shipped, declared));
            })
            .WithTrackingName("EntityPlan");
        IncrementalValueProvider<ScenePlan> scenePlan = scenes
            .Combine(engineScene).Combine(isLogicAssembly).Combine(rootNamespace).Combine(documents).Combine(assets)
            .Select(static (input, _) =>
            {
                var (((((models, engine), logic), root), shipped), assetModels) = input;

                return SceneResolver.Resolve(new SceneInputs(models, engine, logic, root, shipped, assetModels));
            })
            .WithTrackingName("ScenePlan");
        IncrementalValueProvider<InputDriverPlan> driverPlan = drivers
            .Select(static (models, _) => InputDriverResolver.Resolve(models))
            .WithTrackingName("InputDriverPlan");
        IncrementalValueProvider<BootPlan> bootPlan = boot
            .Combine(driverPlan)
            .Select(static (input, _) => BootResolver.Resolve(input.Left, input.Right))
            .WithTrackingName("BootPlan");

        // Every file and every check, one per row.
        context.RegisterSourceOutput(authorableFaults, AuthorableCheck.Report);
        context.RegisterSourceOutput(writableAuthorableFields, AuthorableSuppressionRenderer.Emit);
        context.RegisterSourceOutput(entityPlan, EntityRenderer.Emit);
        context.RegisterSourceOutput(scenePlan.Combine(providerName), SceneRenderer.Emit);
        context.RegisterSourceOutput(scenePlan.Combine(documents), DocumentClaimCheck.Run);
        context.RegisterSourceOutput(driverPlan.Combine(isLogicAssembly), InputDriverRenderer.Emit);
        context.RegisterSourceOutput(providerName, RegistryProviderRenderer.Emit);
        context.RegisterSourceOutput(bootPlan, BootRenderer.Emit);
        context.RegisterSourceOutput(project, GlobalUsingsRenderer.Emit);
        context.RegisterSourceOutput(project, RoleCheck.Report);
    }

    // Syntax only. This runs on every type declaration at every keystroke, and a semantic lookup
    // here would be paid each time. One pass describes every domain for the same reason: a pass per
    // domain would pay this filter and the symbol binding once for each.
    private static bool MayBeRegistered(SyntaxNode node) =>
        node is TypeDeclarationSyntax declaration
        && (declaration.BaseList is not null || declaration.AttributeLists.Count > 0);

    private static RegistryCandidate Describe(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type)
        {
            return default;
        }

        Compilation compilation = context.SemanticModel.Compilation;

        return new RegistryCandidate(
            EntityDescriber.Describe(type, declaration, context.SemanticModel),
            SceneDescriber.Describe(type, declaration, compilation),
            InputDriverDescriber.Describe(type, declaration, compilation));
    }

    // Keys are measured against the declared root namespace, or the assembly name when the project
    // leaves it to MSBuild's default.
    private static IncrementalValueProvider<string> RootNamespace(IncrementalGeneratorInitializationContext context) =>
        context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
                options.GlobalOptions.TryGetValue(MetadataNames.RootNamespaceProperty, out string? declared) && declared.Length > 0
                    ? declared
                    : null)
            .Combine(context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty))
            .Select(static (input, _) => input.Left ?? input.Right);

    // Every CapsuleAssets member the build marked with the attribute, described.
    private static IncrementalValuesProvider<TModel> Marked<TModel>(
        IncrementalGeneratorInitializationContext context, string attribute, Func<GeneratorAttributeSyntaxContext, TModel?> describe)
        where TModel : struct =>
        context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attribute,
                static (node, _) => node is PropertyDeclarationSyntax,
                (marked, _) => describe(marked))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!.Value);

    private static IncrementalValueProvider<EquatableArray<TModel>> Collected<TModel>(IncrementalValuesProvider<TModel> models) =>
        models.Collect().Select(static (items, _) => new EquatableArray<TModel>(items));

    // One domain's models out of the candidates.
    private static IncrementalValueProvider<EquatableArray<TModel>> Collected<TModel>(
        IncrementalValuesProvider<RegistryCandidate> candidates, Func<RegistryCandidate, TModel?> domain)
        where TModel : struct =>
        Collected(candidates.Where(candidate => domain(candidate) is not null).Select((candidate, _) => domain(candidate)!.Value));
}
