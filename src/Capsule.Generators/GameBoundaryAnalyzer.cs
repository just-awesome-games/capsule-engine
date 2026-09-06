using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Generators;

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
        "Game logic cannot use randomness outside the scene's seeded source",
        "'{0}' is not reproducible across runs or runtime versions; draw from the seeded source the scene holds, which an entity or component reaches as Random");

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
        context.RegisterOperationAction(AnalyzeField, OperationKind.FieldReference);
        context.RegisterOperationAction(AnalyzeAwait, OperationKind.Await);
        context.RegisterOperationAction(AnalyzeLock, OperationKind.Lock);
        context.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
        context.RegisterSymbolAction(AnalyzeStoredRandom, SymbolKind.Field, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeNativeImport, SymbolKind.Method);
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
        DiagnosticDescriptor? rule = ClassifyMethod(method);
        if (rule is not null && (rule != Concurrency || operation.Parent is not IAwaitOperation))
        {
            Report(
                context,
                rule,
                operation.Syntax.GetLocation(),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
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
        if (IsExternalIo(constructor) || IsExternalState(constructor))
        {
            Report(context, ExternalIo, operation.Syntax.GetLocation(), display);
        }
        else if (IsConcurrency(constructor))
        {
            Report(context, Concurrency, operation.Syntax.GetLocation(), display);
        }
        else if (IsAmbientTime(constructor))
        {
            Report(context, AmbientTime, operation.Syntax.GetLocation(), display);
        }
        else if (IsSystemType(constructor.ContainingType, "Random"))
        {
            Report(context, AmbientRandom, operation.Syntax.GetLocation(), display);
        }
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context)
    {
        IMethodReferenceOperation operation = (IMethodReferenceOperation)context.Operation;
        IMethodSymbol method = operation.Method;
        DiagnosticDescriptor? rule = ClassifyMethod(method);
        if (rule is not null)
        {
            Report(
                context,
                rule,
                operation.Syntax.GetLocation(),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }
    }

    private static DiagnosticDescriptor? ClassifyMethod(IMethodSymbol method)
    {
        if (IsExternalIo(method) || IsExternalState(method))
        {
            return ExternalIo;
        }

        if (IsConcurrency(method))
        {
            return Concurrency;
        }

        if (IsAmbientTime(method))
        {
            return AmbientTime;
        }

        return IsAmbientRandom(method) ? AmbientRandom : null;
    }

    private static void AnalyzeAwait(OperationAnalysisContext context)
    {
        IAwaitOperation operation = (IAwaitOperation)context.Operation;
        Report(context, Concurrency, operation.Syntax.GetLocation(), "await");
    }

    private static void AnalyzeLock(OperationAnalysisContext context)
    {
        ILockOperation operation = (ILockOperation)context.Operation;
        Report(context, Concurrency, operation.Syntax.GetLocation(), "lock");
    }

    private static void AnalyzeNativeImport(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;
        if (!method.GetAttributes().Any(attribute => IsNativeImportAttribute(attribute.AttributeClass)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            ExternalIo,
            method.Locations.FirstOrDefault() ?? Location.None,
            method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    // Construction is caught at its own site, but a seeded instance can also arrive from
    // outside the assembly; the field or property that keeps it is where holding one is visible.
    private static void AnalyzeStoredRandom(SymbolAnalysisContext context)
    {
        ITypeSymbol stored = context.Symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => throw new InvalidOperationException($"Unexpected symbol kind '{context.Symbol.Kind}'."),
        };

        if (stored is INamedTypeSymbol named && IsSystemType(named, "Random"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AmbientRandom,
                context.Symbol.Locations.FirstOrDefault() ?? Location.None,
                context.Symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
        }
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
        else if (IsSystemType(property.ContainingType, "Random"))
        {
            Report(context, AmbientRandom, operation.Syntax.GetLocation(), display);
        }
        else if (IsExternalIo(property) || IsExternalState(property))
        {
            Report(context, ExternalIo, operation.Syntax.GetLocation(), display);
        }
        else if (IsConcurrency(property))
        {
            Report(context, Concurrency, operation.Syntax.GetLocation(), display);
        }
    }

    private static void AnalyzeField(OperationAnalysisContext context)
    {
        IFieldReferenceOperation operation = (IFieldReferenceOperation)context.Operation;
        if (IsExternalIo(operation.Field))
        {
            Report(
                context,
                ExternalIo,
                operation.Syntax.GetLocation(),
                operation.Field.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }
    }

    private static bool Enabled(AnalyzerOptions options, string property) =>
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(property, out string? value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsMonoGame(string assemblyName) =>
        assemblyName.StartsWith("MonoGame.Framework", StringComparison.Ordinal)
        || string.Equals(assemblyName, "Microsoft.Xna.Framework", StringComparison.Ordinal);

    // The whole namespace tree is banned, so a sub-namespace nobody anticipated stays closed;
    // only members proven to have no external effect are carved back out.
    private static bool IsExternalIo(ISymbol symbol)
    {
        string namespaceName = symbol.ContainingNamespace.ToDisplayString();
        bool external = namespaceName is "System.IO" or "System.Net"
            || namespaceName.StartsWith("System.IO.", StringComparison.Ordinal)
            || namespaceName.StartsWith("System.Net.", StringComparison.Ordinal);

        return external && !TouchesNothingOutside(symbol);
    }

    private static bool TouchesNothingOutside(ISymbol symbol)
    {
        string namespaceName = symbol.ContainingNamespace.ToDisplayString();
        INamedTypeSymbol? type = symbol.ContainingType;
        if (namespaceName == "System.IO.Enumeration")
        {
            return type?.Name == "FileSystemName";
        }

        if (namespaceName != "System.IO")
        {
            return false;
        }

        // GetRandomFileName is exempt here so CAP105 reports it as ambient randomness instead.
        if (type?.Name == "Path")
        {
            return !IsAmbientPath(symbol);
        }

        if (type?.Name is "MemoryStream" or "StringReader" or "StringWriter" or "BufferedStream"
            or "Stream" or "TextReader" or "TextWriter")
        {
            return true;
        }

        // A reader or writer over a stream is only as external as that stream, which is judged
        // where it is created; one opened from a path opens the file itself.
        return type?.Name is "BinaryReader" or "BinaryWriter" or "StreamReader" or "StreamWriter"
            && (symbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                || constructor.Parameters.Length == 0
                || constructor.Parameters[0].Type.SpecialType != SpecialType.System_String);
    }

    private static bool IsAmbientPath(ISymbol symbol)
    {
        if (symbol is IFieldSymbol)
        {
            return symbol.Name is "DirectorySeparatorChar" or "AltDirectorySeparatorChar"
                or "VolumeSeparatorChar" or "PathSeparator";
        }

        return symbol is IMethodSymbol method
            && (method.Name is "Exists" or "GetTempFileName" or "GetTempPath"
                or "GetInvalidFileNameChars" or "GetInvalidPathChars"
                || (method.Name == "GetFullPath" && method.Parameters.Length == 1));
    }

    private static bool IsConcurrency(ISymbol symbol)
    {
        INamedTypeSymbol? type = symbol.ContainingType;
        string namespaceName = symbol.ContainingNamespace.ToDisplayString();
        if (namespaceName == "System.Timers")
        {
            return type?.Name == "Timer";
        }

        bool threading = namespaceName == "System.Threading"
            || namespaceName.StartsWith("System.Threading.", StringComparison.Ordinal);

        return threading && !NeitherSchedulesNorBlocks(symbol);
    }

    private static bool NeitherSchedulesNorBlocks(ISymbol symbol)
    {
        INamedTypeSymbol? type = symbol.ContainingType;
        string namespaceName = symbol.ContainingNamespace.ToDisplayString();
        if (namespaceName == "System.Threading")
        {
            return type?.Name is "Interlocked" or "Volatile" or "CancellationToken" or "CancellationTokenSource";
        }

        // Every other Task member either queues work or waits on it; construction does both.
        return namespaceName == "System.Threading.Tasks"
            && type?.Name == "Task"
            && symbol.Name is "FromResult" or "CompletedTask" or "FromException" or "FromCanceled"
                or "WhenAll" or "WhenAny";
    }

    private static bool IsNativeImportAttribute(INamedTypeSymbol? type) =>
        type?.Name is "DllImportAttribute" or "LibraryImportAttribute"
        && type.ContainingNamespace.ToDisplayString() == "System.Runtime.InteropServices";

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
        IsSystemType(symbol.ContainingType, "Random")
        || (symbol.ContainingType?.Name == "Path"
            && symbol.ContainingNamespace.ToDisplayString() == "System.IO"
            && symbol.Name == "GetRandomFileName")
        || (IsSystemType(symbol.ContainingType, "Guid") && symbol.Name == "NewGuid")
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
