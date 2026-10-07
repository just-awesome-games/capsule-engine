using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GameBoundaryAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [
            Diagnostics.RuntimeBoundary,
            Diagnostics.PlatformBoundary,
            Diagnostics.ExternalIo,
            Diagnostics.Concurrency,
            Diagnostics.AmbientTime,
            Diagnostics.AmbientRandom,
            Diagnostics.PlatformMath,
            Diagnostics.Reflection,
        ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext context)
    {
        GeneratorRole role = GeneratorRoles.Read(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        if (role == GeneratorRole.None)
        {
            return;
        }

        bool logic = role.DeclaresLogic();

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
                    Diagnostics.PlatformBoundary,
                    Location.None,
                    context.Compilation.AssemblyName,
                    reference.Name));
            }
            else if (logic && string.Equals(reference.Name, "Capsule.Runtime", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.RuntimeBoundary,
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
        if (rule is not null && (rule != Diagnostics.Concurrency || operation.Parent is not IAwaitOperation))
        {
            Report(context, rule, operation.Syntax.GetLocation(), method);
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        IObjectCreationOperation operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor is { } constructor && Classify(new Subject(constructor)) is { } rule)
        {
            // Report the type, since the call site reads as 'new Random(...)'.
            Report(context, rule, operation.Syntax.GetLocation(), Display(constructor.ContainingType));
        }
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context)
    {
        IMethodReferenceOperation operation = (IMethodReferenceOperation)context.Operation;
        IMethodSymbol method = operation.Method;
        if (Classify(new Subject(method)) is { } rule)
        {
            Report(context, rule, operation.Syntax.GetLocation(), method);
        }
    }

    private static void AnalyzeProperty(OperationAnalysisContext context)
    {
        IPropertyReferenceOperation operation = (IPropertyReferenceOperation)context.Operation;
        if (Classify(new Subject(operation.Property)) is { } rule)
        {
            Report(context, rule, operation.Syntax.GetLocation(), Display(operation.Property));
        }
    }

    private static void AnalyzeField(OperationAnalysisContext context)
    {
        IFieldReferenceOperation operation = (IFieldReferenceOperation)context.Operation;
        if (IsExternalIo(new Subject(operation.Field)))
        {
            Report(context, Diagnostics.ExternalIo, operation.Syntax.GetLocation(), Display(operation.Field));
        }
    }

    private static void AnalyzeAwait(OperationAnalysisContext context) =>
        Report(context, Diagnostics.Concurrency, context.Operation.Syntax.GetLocation(), "await");

    private static void AnalyzeLock(OperationAnalysisContext context) =>
        Report(context, Diagnostics.Concurrency, context.Operation.Syntax.GetLocation(), "lock");

    private static void AnalyzeNativeImport(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;
        if (method.GetAttributes().Any(attribute => IsNativeImportAttribute(attribute.AttributeClass)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ExternalIo,
                method.Locations.FirstOrDefault() ?? Location.None,
                Display(method)));
        }
    }

    // Construction is caught at its own site, but a seeded instance can also arrive from outside the
    // assembly. The field or property that keeps it is where holding one becomes visible.
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
                Diagnostics.AmbientRandom,
                context.Symbol.Locations.FirstOrDefault() ?? Location.None,
                Display(context.Symbol)));
        }
    }

    // Time and randomness are judged first. Environment.TickCount is external state too, and the narrower
    // rule names what to reach for instead.
    private static DiagnosticDescriptor? Classify(in Subject subject) =>
        IsAmbientTime(subject) ? Diagnostics.AmbientTime
        : IsAmbientRandom(subject) ? Diagnostics.AmbientRandom
        : IsExternalIo(subject) || IsExternalState(subject) ? Diagnostics.ExternalIo
        : subject.Under("System.Reflection") ? Diagnostics.Reflection
        : IsConcurrency(subject) ? Diagnostics.Concurrency
        : IsPlatformMath(subject) ? Diagnostics.PlatformMath
        : null;

    private static bool IsMonoGame(string assemblyName) =>
        assemblyName.StartsWith("MonoGame.Framework", StringComparison.Ordinal)
        || string.Equals(assemblyName, "Microsoft.Xna.Framework", StringComparison.Ordinal);

    // The namespace tree is banned wholesale, keeping an unanticipated sub-namespace closed. Only
    // members proven to have no external effect are carved back out.
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

        // A reader or writer over a stream is only as external as that stream, judged where the
        // stream is created. One opened from a path opens the file itself.
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

        // Every other Task member queues work or waits on it, and construction does both.
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

    // Exactly the functions DeterministicMath replaces, so every report names its replacement. The
    // double overloads are refused too, since Math.Sin is the habitual call, though a float twin is no
    // drop-in for them. A function DeterministicMath gains later joins this list.
    private static bool IsPlatformMath(in Subject subject) =>
        subject.Symbol.Name is "Sin" or "Cos" or "SinCos" or "Tan" or "Asin" or "Acos" or "Atan" or "Atan2"
            or "Exp" or "Exp2" or "Log" or "Log2" or "Log10" or "Pow"
        && subject.Symbol is IMethodSymbol
        && (subject.IsSystem("MathF") || subject.IsSystem("Math") || subject.IsSystem("Single") || subject.IsSystem("Double"));

    private static string Replacement(IMethodSymbol method) => method switch
    {
        { Name: "SinCos" } => "DeterministicMath.Sin and DeterministicMath.Cos",
        { Name: "Log", Parameters.Length: 2 } =>
            $"DeterministicMath.Log({method.Parameters[0].Name}) / DeterministicMath.Log({method.Parameters[1].Name})",
        _ => "DeterministicMath." + method.Name,
    };

    private static bool IsExternalState(in Subject subject) =>
        subject.IsSystem("Console")
        || subject.IsSystem("Environment")
        || (subject.Type?.Name == "Process" && subject.In("System.Diagnostics"));

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, Location location, string display) =>
        context.ReportDiagnostic(Diagnostic.Create(rule, location, display));

    // Only CAP107's message has a second placeholder, for the replacement it names.
    private static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, Location location, IMethodSymbol method) =>
        context.ReportDiagnostic(rule == Diagnostics.PlatformMath
            ? Diagnostic.Create(rule, location, Display(method), Replacement(method))
            : Diagnostic.Create(rule, location, Display(method)));

    // Every rule asks the same things of a symbol, and spelling a namespace out allocates a string.
    // Spell it once per operation here for all of them to read.
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
