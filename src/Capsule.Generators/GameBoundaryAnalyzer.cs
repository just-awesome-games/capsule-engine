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
        "'{0}' performs external I/O; the build reads what a game ships and hands it over as CapsuleAssets, and the shell owns every other file, socket and device");

    private static readonly DiagnosticDescriptor Concurrency = Rule(
        ConcurrencyId,
        "Game logic cannot schedule ambient concurrency",
        "'{0}' schedules work outside the deterministic simulation; do the work inside the step instead");

    private static readonly DiagnosticDescriptor AmbientTime = Rule(
        AmbientTimeId,
        "Game logic cannot read ambient time",
        "'{0}' reads process or wall-clock time; use the simulation time the step is given, which a scene, entity or component reaches as StepContext.TotalSeconds or StepContext.DeltaSeconds");

    private static readonly DiagnosticDescriptor AmbientRandom = Rule(
        AmbientRandomId,
        "Game logic cannot use randomness outside the run's seeded source",
        "'{0}' is not reproducible across runs or runtime versions; draw from the seeded source the run holds, which a scene, entity or component reaches as Random");

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
        DiagnosticDescriptor? rule = Classify(new Subject(method));
        if (rule is not null && (rule != Concurrency || operation.Parent is not IAwaitOperation))
        {
            Report(context, rule, operation.Syntax.GetLocation(), Display(method));
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        IObjectCreationOperation operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor is { } constructor && Classify(new Subject(constructor)) is { } rule)
        {
            // The type, not the constructor: a call site reads as 'new Random(...)'.
            Report(context, rule, operation.Syntax.GetLocation(), Display(constructor.ContainingType));
        }
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context)
    {
        IMethodReferenceOperation operation = (IMethodReferenceOperation)context.Operation;
        IMethodSymbol method = operation.Method;
        if (Classify(new Subject(method)) is { } rule)
        {
            Report(context, rule, operation.Syntax.GetLocation(), Display(method));
        }
    }

    private static void AnalyzeProperty(OperationAnalysisContext context)
    {
        IPropertyReferenceOperation operation = (IPropertyReferenceOperation)context.Operation;
        Subject subject = new(operation.Property);

        // Time and randomness are judged first: the types they live on are external state too, and
        // the narrower rule is the one that names what to reach for instead.
        DiagnosticDescriptor? rule = IsAmbientTime(subject) ? AmbientTime
            : subject.IsSystem("Random") ? AmbientRandom
            : IsExternalIo(subject) || IsExternalState(subject) ? ExternalIo
            : IsConcurrency(subject) ? Concurrency
            : null;

        if (rule is not null)
        {
            Report(context, rule, operation.Syntax.GetLocation(), Display(operation.Property));
        }
    }

    private static void AnalyzeField(OperationAnalysisContext context)
    {
        IFieldReferenceOperation operation = (IFieldReferenceOperation)context.Operation;
        if (IsExternalIo(new Subject(operation.Field)))
        {
            Report(context, ExternalIo, operation.Syntax.GetLocation(), Display(operation.Field));
        }
    }

    private static void AnalyzeAwait(OperationAnalysisContext context) =>
        Report(context, Concurrency, context.Operation.Syntax.GetLocation(), "await");

    private static void AnalyzeLock(OperationAnalysisContext context) =>
        Report(context, Concurrency, context.Operation.Syntax.GetLocation(), "lock");

    private static void AnalyzeNativeImport(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;
        if (method.GetAttributes().Any(attribute => IsNativeImportAttribute(attribute.AttributeClass)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ExternalIo,
                method.Locations.FirstOrDefault() ?? Location.None,
                Display(method)));
        }
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

        if (stored.Name == "Random" && stored.ContainingNamespace.ToDisplayString() == "System")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AmbientRandom,
                context.Symbol.Locations.FirstOrDefault() ?? Location.None,
                Display(context.Symbol)));
        }
    }

    private static DiagnosticDescriptor? Classify(in Subject subject)
    {
        if (IsExternalIo(subject) || IsExternalState(subject))
        {
            return ExternalIo;
        }

        if (IsConcurrency(subject))
        {
            return Concurrency;
        }

        if (IsAmbientTime(subject))
        {
            return AmbientTime;
        }

        return IsAmbientRandom(subject) ? AmbientRandom : null;
    }

    private static bool Enabled(AnalyzerOptions options, string property) =>
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(property, out string? value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsMonoGame(string assemblyName) =>
        assemblyName.StartsWith("MonoGame.Framework", StringComparison.Ordinal)
        || string.Equals(assemblyName, "Microsoft.Xna.Framework", StringComparison.Ordinal);

    // The whole namespace tree is banned, so a sub-namespace nobody anticipated stays closed;
    // only members proven to have no external effect are carved back out.
    private static bool IsExternalIo(in Subject subject) =>
        (subject.Under("System.IO") || subject.Under("System.Net")) && !TouchesNothingOutside(subject);

    private static bool TouchesNothingOutside(in Subject subject)
    {
        if (subject.In("System.IO.Enumeration"))
        {
            return subject.Type?.Name == "FileSystemName";
        }

        if (!subject.In("System.IO"))
        {
            return false;
        }

        // GetRandomFileName is exempt here so CAP105 reports it as ambient randomness instead.
        if (subject.Type?.Name == "Path")
        {
            return !IsAmbientPath(subject.Symbol);
        }

        if (subject.Type?.Name is "MemoryStream" or "StringReader" or "StringWriter" or "BufferedStream"
            or "Stream" or "TextReader" or "TextWriter")
        {
            return true;
        }

        // A reader or writer over a stream is only as external as that stream, which is judged
        // where it is created; one opened from a path opens the file itself.
        return subject.Type?.Name is "BinaryReader" or "BinaryWriter" or "StreamReader" or "StreamWriter"
            && (subject.Symbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
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

    private static bool IsConcurrency(in Subject subject) =>
        subject.In("System.Timers")
            ? subject.Type?.Name == "Timer"
            : subject.Under("System.Threading") && !NeitherSchedulesNorBlocks(subject);

    private static bool NeitherSchedulesNorBlocks(in Subject subject)
    {
        if (subject.In("System.Threading"))
        {
            return subject.Type?.Name is "Interlocked" or "Volatile" or "CancellationToken" or "CancellationTokenSource";
        }

        // Every other Task member either queues work or waits on it; construction does both.
        return subject.In("System.Threading.Tasks")
            && subject.Type?.Name == "Task"
            && subject.Symbol.Name is "FromResult" or "CompletedTask" or "FromException" or "FromCanceled"
                or "WhenAll" or "WhenAny";
    }

    private static bool IsNativeImportAttribute(INamedTypeSymbol? type) =>
        type?.Name is "DllImportAttribute" or "LibraryImportAttribute"
        && type.ContainingNamespace.ToDisplayString() == "System.Runtime.InteropServices";

    private static bool IsAmbientTime(in Subject subject)
    {
        if ((subject.IsSystem("DateTime") || subject.IsSystem("DateTimeOffset"))
            && subject.Symbol.Name is "Now" or "UtcNow" or "Today")
        {
            return true;
        }

        if (subject.IsSystem("Environment") && subject.Symbol.Name is "TickCount" or "TickCount64")
        {
            return true;
        }

        if (subject.IsSystem("TimeProvider") && subject.Symbol.Name == "System")
        {
            return true;
        }

        return subject.Type?.Name == "Stopwatch" && subject.In("System.Diagnostics");
    }

    private static bool IsAmbientRandom(in Subject subject) =>
        subject.IsSystem("Random")
        || (subject.Type?.Name == "Path" && subject.In("System.IO") && subject.Symbol.Name == "GetRandomFileName")
        || (subject.IsSystem("Guid") && subject.Symbol.Name == "NewGuid")
        || (subject.Type?.Name == "RandomNumberGenerator" && subject.In("System.Security.Cryptography"));

    private static bool IsExternalState(in Subject subject) =>
        subject.IsSystem("Console")
        || subject.IsSystem("Environment")
        || (subject.Type?.Name == "Process" && subject.In("System.Diagnostics"))
        || subject.Under("System.Reflection");

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, Location location, string display) =>
        context.ReportDiagnostic(Diagnostic.Create(rule, location, display));

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(
            id, title, message, "Capsule.Architecture", DiagnosticSeverity.Error, true, null,
            CapsuleDocs.At(CapsuleDocs.LogicBoundary));

    // Every rule asks the same things of a symbol, and spelling a namespace out allocates a string;
    // spelt once per operation here and read by all of them.
    private readonly struct Subject(ISymbol symbol)
    {
        internal ISymbol Symbol { get; } = symbol;

        internal INamedTypeSymbol? Type { get; } = symbol.ContainingType;

        internal string Space { get; } = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        internal bool IsSystem(string name) => Type?.Name == name && In("System");

        internal bool In(string space) => string.Equals(Space, space, StringComparison.Ordinal);

        internal bool Under(string space) =>
            In(space)
            || (Space.Length > space.Length
                && Space[space.Length] == '.'
                && Space.StartsWith(space, StringComparison.Ordinal));
    }
}
