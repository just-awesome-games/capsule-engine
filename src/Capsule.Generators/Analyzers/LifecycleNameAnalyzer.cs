using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Generators;

// A lifecycle name from another convention compiles on an entity, component or scene and never runs. The
// rule reports one only where it is provably dead: an override of nothing, or a private method its type
// never references. A public or protected method of such a name is the game's own API.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LifecycleNameAnalyzer : DiagnosticAnalyzer
{
    private const string MemberHooks =
        "Capsule calls OnStart, OnStep, OnLateStep, OnAddedToScene and OnRemovedFromScene, and contacts arrive through a collider's ContactEntered and ContactExited events";

    private const string SceneHooks = "Capsule calls a scene's OnStart, OnStep, OnLateStep and OnStop";

    private static readonly ImmutableHashSet<string> Names = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Update", "FixedUpdate", "LateUpdate", "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy",
        "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D",
        "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",
        "_Ready", "_Process", "_PhysicsProcess", "_Input", "_EnterTree", "_ExitTree");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Diagnostics.NeverCalledLifecycleMethod];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext context)
    {
        Compilation compilation = context.Compilation;
        if (!GeneratorRoles.Read(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions).DeclaresLogic()
            || compilation.GetTypeByMetadataName(MetadataNames.Scene) is not { } scene)
        {
            return;
        }

        INamedTypeSymbol?[] members =
        [
            compilation.GetTypeByMetadataName(MetadataNames.Entity),
            compilation.GetTypeByMetadataName(MetadataNames.Component),
        ];

        // A private method is reachable from its type's partial declarations and nested types at any depth,
        // which each run as their own symbol. References are gathered across the compilation, keyed by the
        // cheap private-and-named filter, and judged once every operation has been seen.
        ConcurrentDictionary<IMethodSymbol, string> candidates = new(SymbolEqualityComparer.Default);
        ConcurrentDictionary<IMethodSymbol, byte> referenced = new(SymbolEqualityComparer.Default);

        context.RegisterSymbolAction(
            symbolContext =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)symbolContext.Symbol;
                string? hooks = SymbolShape.Inherits(type, scene) ? SceneHooks
                    : members.Any(member => member is not null && SymbolShape.Inherits(type, member)) ? MemberHooks
                    : null;
                if (hooks is not null)
                {
                    Collect(symbolContext, type, hooks, candidates);
                }
            },
            SymbolKind.NamedType);

        context.RegisterOperationAction(
            operationContext =>
            {
                IMethodSymbol target = operationContext.Operation is IInvocationOperation invocation
                    ? invocation.TargetMethod
                    : ((IMethodReferenceOperation)operationContext.Operation).Method;
                if (target.DeclaredAccessibility == Accessibility.Private && Names.Contains(target.Name))
                {
                    referenced.TryAdd(target.OriginalDefinition, 0);
                }
            },
            OperationKind.Invocation,
            OperationKind.MethodReference);

        context.RegisterCompilationEndAction(endContext =>
        {
            foreach (KeyValuePair<IMethodSymbol, string> candidate in candidates)
            {
                if (!referenced.ContainsKey(candidate.Key))
                {
                    endContext.ReportDiagnostic(Create(candidate.Key, candidate.Value));
                }
            }
        });
    }

    // Reports an override of nothing at once and holds each private method of a listed name for the end.
    private static void Collect(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        string hooks,
        ConcurrentDictionary<IMethodSymbol, string> candidates)
    {
        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method && Names.Contains(method.Name))
            {
                if (method.IsOverride && method.OverriddenMethod is null)
                {
                    context.ReportDiagnostic(Create(method, hooks));
                }
                else if (method.DeclaredAccessibility == Accessibility.Private)
                {
                    candidates.TryAdd(method, hooks);
                }
            }
        }
    }

    private static Diagnostic Create(IMethodSymbol method, string hooks) =>
        Diagnostic.Create(
            Diagnostics.NeverCalledLifecycleMethod,
            method.Locations.FirstOrDefault() ?? Location.None,
            method.Name,
            hooks);
}
