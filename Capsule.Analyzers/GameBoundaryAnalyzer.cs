using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Analyzers;

/// <summary>
/// Keeps deterministic game logic independent from the runtime substrate and common ambient
/// sources of nondeterminism. Capsule projects opt into a role through MSBuild; consumers do not
/// configure individual rules.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GameBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string RuntimeBoundaryId = "CAP100";
    public const string PlatformBoundaryId = "CAP101";
    public const string ExternalIoId = "CAP102";
    public const string ConcurrencyId = "CAP103";
    public const string AmbientTimeId = "CAP104";
    public const string AmbientRandomId = "CAP105";

    private static readonly DiagnosticDescriptor RuntimeBoundary = Rule(
        RuntimeBoundaryId,
        "Game logic cannot reference the runtime",
        "Game-logic assembly '{0}' references '{1}'; runtime access belongs in the shell");

    private static readonly DiagnosticDescriptor PlatformBoundary = Rule(
        PlatformBoundaryId,
        "Game projects cannot reference MonoGame directly",
        "Capsule project '{0}' references '{1}' directly; platform APIs belong behind Capsule.Runtime");

    private static readonly DiagnosticDescriptor ExternalIo = Rule(
        ExternalIoId,
        "Game logic cannot perform external I/O",
        "'{0}' performs external I/O; move it behind the shell/runtime boundary");

    private static readonly DiagnosticDescriptor Concurrency = Rule(
        ConcurrencyId,
        "Game logic cannot schedule ambient concurrency",
        "'{0}' schedules work outside the deterministic simulation");

    private static readonly DiagnosticDescriptor AmbientTime = Rule(
        AmbientTimeId,
        "Game logic cannot read ambient time",
        "'{0}' reads process or wall-clock time; use the simulation time supplied by Capsule");

    private static readonly DiagnosticDescriptor AmbientRandom = Rule(
        AmbientRandomId,
        "Game logic cannot use ambient randomness",
        "'{0}' is not reproducible; use an explicitly seeded random source owned by game state");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [RuntimeBoundary, PlatformBoundary, ExternalIo, Concurrency, AmbientTime, AmbientRandom];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext context)
    {
        bool logic = Enabled(context.Options, "build_property.CapsuleGameLogic");
        bool shell = Enabled(context.Options, "build_property.CapsuleGameShell");
        if (!logic && !shell)
        {
            return;
        }

        context.RegisterCompilationEndAction(compilation => AnalyzeReferences(compilation, logic));
        if (!logic)
        {
            return;
        }

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeProperty, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeAwait, OperationKind.Await);
    }

    private static void AnalyzeReferences(CompilationAnalysisContext context, bool logic)
    {
        foreach (AssemblyIdentity reference in context.Compilation.ReferencedAssemblyNames)
        {
            if (IsMonoGame(reference.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PlatformBoundary,
                    Location.None,
                    context.Compilation.AssemblyName,
                    reference.Name));
            }
            else if (logic && string.Equals(reference.Name, "Capsule.Runtime", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuntimeBoundary,
                    Location.None,
                    context.Compilation.AssemblyName,
                    reference.Name));
            }
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        IInvocationOperation operation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = operation.TargetMethod;
        string display = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (IsExternalIo(method.ContainingNamespace) || IsExternalState(method))
        {
            Report(context, ExternalIo, operation.Syntax.GetLocation(), display);
        }
        else if (IsConcurrency(method.ContainingNamespace) && operation.Parent is not IAwaitOperation)
        {
            Report(context, Concurrency, operation.Syntax.GetLocation(), display);
        }
        else if (IsAmbientTime(method))
        {
            Report(context, AmbientTime, operation.Syntax.GetLocation(), display);
        }
        else if (IsAmbientRandom(method))
        {
            Report(context, AmbientRandom, operation.Syntax.GetLocation(), display);
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        IObjectCreationOperation operation = (IObjectCreationOperation)context.Operation;
        IMethodSymbol? constructor = operation.Constructor;
        if (constructor is null)
        {
            return;
        }

        string display = constructor.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (IsExternalIo(constructor.ContainingNamespace) || IsExternalState(constructor))
        {
            Report(context, ExternalIo, operation.Syntax.GetLocation(), display);
        }
        else if (IsConcurrency(constructor.ContainingNamespace))
        {
            Report(context, Concurrency, operation.Syntax.GetLocation(), display);
        }
        else if (IsAmbientTime(constructor))
        {
            Report(context, AmbientTime, operation.Syntax.GetLocation(), display);
        }
        else if (IsSystemType(constructor.ContainingType, "Random") && constructor.Parameters.Length == 0)
        {
            Report(context, AmbientRandom, operation.Syntax.GetLocation(), display);
        }
    }

    private static void AnalyzeAwait(OperationAnalysisContext context)
    {
        IAwaitOperation operation = (IAwaitOperation)context.Operation;
        Report(context, Concurrency, operation.Syntax.GetLocation(), "await");
    }

    private static void AnalyzeProperty(OperationAnalysisContext context)
    {
        IPropertyReferenceOperation operation = (IPropertyReferenceOperation)context.Operation;
        IPropertySymbol property = operation.Property;
        string display = property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (IsAmbientTime(property))
        {
            Report(context, AmbientTime, operation.Syntax.GetLocation(), display);
        }
        else if (IsSystemType(property.ContainingType, "Random") && property.Name == "Shared")
        {
            Report(context, AmbientRandom, operation.Syntax.GetLocation(), display);
        }
        else if (IsExternalState(property))
        {
            Report(context, ExternalIo, operation.Syntax.GetLocation(), display);
        }
    }

    private static bool Enabled(AnalyzerOptions options, string property) =>
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(property, out string? value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsMonoGame(string assemblyName) =>
        assemblyName.StartsWith("MonoGame.Framework", StringComparison.Ordinal)
        || string.Equals(assemblyName, "Microsoft.Xna.Framework", StringComparison.Ordinal);

    private static bool IsExternalIo(INamespaceSymbol? value)
    {
        string name = value?.ToDisplayString() ?? string.Empty;
        return name == "System.IO" || name.StartsWith("System.IO.", StringComparison.Ordinal)
            || name == "System.Net" || name.StartsWith("System.Net.", StringComparison.Ordinal);
    }

    private static bool IsConcurrency(INamespaceSymbol? value)
    {
        string name = value?.ToDisplayString() ?? string.Empty;
        return name == "System.Threading" || name.StartsWith("System.Threading.", StringComparison.Ordinal);
    }

    private static bool IsAmbientTime(ISymbol symbol)
    {
        INamedTypeSymbol type = symbol.ContainingType;
        if ((IsSystemType(type, "DateTime") || IsSystemType(type, "DateTimeOffset"))
            && symbol.Name is "Now" or "UtcNow" or "Today")
        {
            return true;
        }

        if (IsSystemType(type, "Environment") && symbol.Name is "TickCount" or "TickCount64")
        {
            return true;
        }

        if (IsSystemType(type, "TimeProvider") && symbol.Name == "System")
        {
            return true;
        }

        return type.Name == "Stopwatch"
            && type.ContainingNamespace.ToDisplayString() == "System.Diagnostics";
    }

    private static bool IsAmbientRandom(ISymbol symbol) =>
        (IsSystemType(symbol.ContainingType, "Guid") && symbol.Name == "NewGuid")
        || (symbol.ContainingType?.Name == "RandomNumberGenerator"
            && symbol.ContainingNamespace.ToDisplayString() == "System.Security.Cryptography");

    private static bool IsExternalState(ISymbol symbol)
    {
        INamedTypeSymbol? type = symbol.ContainingType;
        if (IsSystemType(type, "Console") || IsSystemType(type, "Environment"))
        {
            return true;
        }

        string namespaceName = symbol.ContainingNamespace.ToDisplayString();
        return (type?.Name == "Process" && namespaceName == "System.Diagnostics")
            || namespaceName == "System.Reflection"
            || namespaceName.StartsWith("System.Reflection.", StringComparison.Ordinal);
    }

    private static bool IsSystemType(INamedTypeSymbol? type, string name) =>
        type?.Name == name && type.ContainingNamespace.ToDisplayString() == "System";

    private static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, Location location, string display) =>
        context.ReportDiagnostic(Diagnostic.Create(rule, location, display));

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "Capsule.Architecture", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
