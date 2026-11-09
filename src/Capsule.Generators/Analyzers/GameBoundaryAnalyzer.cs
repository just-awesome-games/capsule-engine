using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GameBoundaryAnalyzer : DiagnosticAnalyzer
{
    private const string LogFix = "Write through Capsule.Diagnostics.Log";
    private const string SaveFix = "Persist through Run.Saves. Ship read-only data as assets";
    private const string ShellFix = "Leave sockets, processes, devices and the environment to the shell";
    private const string WaitFix = "Wait with a Countdown its owner steps, or await Wait values in an async Routine a Sequence steps";
    private const string StepFix = "Do the work inside the step";
    private const string PlainMathFix = "Write it as plain arithmetic, which every machine rounds the same";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [
            Diagnostics.RuntimeBoundary,
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
        if (!GeneratorRoles.Read(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions).DeclaresLogic())
        {
            return;
        }

        context.RegisterCompilationEndAction(AnalyzeReferences);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeProperty, OperationKind.PropertyReference);
        context.RegisterOperationAction(AnalyzeField, OperationKind.FieldReference);
        context.RegisterOperationAction(AnalyzeAwait, OperationKind.Await);
        context.RegisterOperationAction(AnalyzeLock, OperationKind.Lock);
        context.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
        context.RegisterOperationAction(AnalyzeOperator, OperationKind.Binary, OperationKind.CompoundAssignment);
        context.RegisterSymbolAction(AnalyzeStoredRandom, SymbolKind.Field, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeNativeImport, SymbolKind.Method);
    }

    private static void AnalyzeReferences(CompilationAnalysisContext context)
    {
        foreach (AssemblyIdentity reference in context.Compilation.ReferencedAssemblyNames)
        {
            if (string.Equals(reference.Name, "Capsule.Runtime", StringComparison.Ordinal))
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
        Subject subject = new(operation.TargetMethod);
        DiagnosticDescriptor? rule = Classify(subject);
        if (rule is not null && (rule != Diagnostics.Concurrency || operation.Parent is not IAwaitOperation))
        {
            Report(context, rule, operation.Syntax.GetLocation(), subject, Display(subject.Symbol));
        }
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        IObjectCreationOperation operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor is { } constructor && new Subject(constructor) is var subject && Classify(subject) is { } rule)
        {
            // Report the type, since the call site reads as 'new Random(...)'.
            Report(context, rule, operation.Syntax.GetLocation(), subject, Display(constructor.ContainingType));
        }
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context)
    {
        IMethodReferenceOperation operation = (IMethodReferenceOperation)context.Operation;
        Subject subject = new(operation.Method);
        if (Classify(subject) is { } rule)
        {
            Report(context, rule, operation.Syntax.GetLocation(), subject, Display(subject.Symbol));
        }
    }

    private static void AnalyzeOperator(OperationAnalysisContext context)
    {
        IMethodSymbol? method = context.Operation switch
        {
            IBinaryOperation binary => binary.OperatorMethod,
            ICompoundAssignmentOperation assignment => assignment.OperatorMethod,
            _ => null,
        };

        if (method is not null && new Subject(method) is var subject && IsPlatformMath(subject))
        {
            Report(context, Diagnostics.PlatformMath, context.Operation.Syntax.GetLocation(), subject, Display(method));
        }
    }

    private static void AnalyzeProperty(OperationAnalysisContext context)
    {
        IPropertyReferenceOperation operation = (IPropertyReferenceOperation)context.Operation;
        Subject subject = new(operation.Property);
        if (Classify(subject) is { } rule)
        {
            Report(context, rule, operation.Syntax.GetLocation(), subject, Display(subject.Symbol));
        }
    }

    private static void AnalyzeField(OperationAnalysisContext context)
    {
        IFieldReferenceOperation operation = (IFieldReferenceOperation)context.Operation;
        Subject subject = new(operation.Field);
        if (IsExternalIo(subject))
        {
            Report(context, Diagnostics.ExternalIo, operation.Syntax.GetLocation(), subject, Display(subject.Symbol));
        }
    }

    // A routine's awaits of waits and other routines resume only inside its sequence's step. They schedule nothing.
    private static void AnalyzeAwait(OperationAnalysisContext context)
    {
        IAwaitOperation operation = (IAwaitOperation)context.Operation;
        ITypeSymbol? awaited = operation.Operation.Type;
        if ((IsAnimationType(awaited, "Wait") || IsAnimationType(awaited, "Routine"))
            && IsAnimationType(EnclosingReturnType(operation, context.ContainingSymbol), "Routine"))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.Concurrency, operation.Syntax.GetLocation(), "await", WaitFix));
    }

    private static bool IsAnimationType(ITypeSymbol? type, string name) =>
        type?.Name == name && type.ContainingNamespace?.ToDisplayString() == "Capsule.Animation";

    private static ITypeSymbol? EnclosingReturnType(IOperation operation, ISymbol containing)
    {
        for (IOperation? parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IAnonymousFunctionOperation lambda:
                    return lambda.Symbol.ReturnType;
                case ILocalFunctionOperation local:
                    return local.Symbol.ReturnType;
            }
        }

        return (containing as IMethodSymbol)?.ReturnType;
    }

    private static void AnalyzeLock(OperationAnalysisContext context) =>
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.Concurrency, context.Operation.Syntax.GetLocation(), "lock", StepFix));

    private static void AnalyzeNativeImport(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;
        if (method.GetAttributes().Any(attribute => IsNativeImportAttribute(attribute.AttributeClass)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ExternalIo,
                method.Locations.FirstOrDefault() ?? Location.None,
                Display(method),
                ShellFix));
        }
    }

    // Construction is caught at its own site, but a seeded instance can also arrive from outside the
    // assembly. The field or property that keeps it is where holding one becomes visible.
    private static void AnalyzeStoredRandom(SymbolAnalysisContext context)
    {
        ITypeSymbol stored = context.Symbol is IFieldSymbol field ? field.Type : ((IPropertySymbol)context.Symbol).Type;

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
        : IsReflection(subject) ? Diagnostics.Reflection
        : IsConcurrency(subject) ? Diagnostics.Concurrency
        : IsPlatformMath(subject) ? Diagnostics.PlatformMath
        : null;

    // System.Type's methods and System.Activator reach members and types by name from outside the System.Reflection
    // tree. A property of System.Type describes the type in hand and stays allowed.
    private static bool IsReflection(in Subject subject) =>
        subject.Under("System.Reflection")
        || (subject.IsSystem("Type") && subject.Symbol is IMethodSymbol)
        || subject.IsSystem("Activator");

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

    private static bool IsPlatformMath(in Subject subject) =>
        subject.Symbol is IMethodSymbol method && (IsTranscendental(subject) || IsEstimate(subject) || IsFusedVectorMath(subject, method));

    // Exactly the functions DeterministicMath replaces. Every report names its replacement. The
    // double overloads are refused too, since Math.Sin is the habitual call, though a float twin is no
    // drop-in for them. A function DeterministicMath gains later joins this list.
    private static bool IsTranscendental(in Subject subject) =>
        subject.Symbol.Name is "Sin" or "Cos" or "SinCos" or "Tan" or "Asin" or "Acos" or "Atan" or "Atan2"
            or "Exp" or "Exp2" or "Log" or "Log2" or "Log10" or "Pow"
        && IsSystemMath(subject);

    // The runtime resolves these by the build and the processor. Fused or not, a result differs in its last bits.
    private static bool IsEstimate(in Subject subject) =>
        subject.Symbol.Name is "Lerp" or "MultiplyAddEstimate" or "ReciprocalEstimate" or "ReciprocalSqrtEstimate"
        && IsSystemMath(subject);

    private static bool IsSystemMath(in Subject subject) =>
        subject.IsSystem("MathF") || subject.IsSystem("Math") || subject.IsSystem("Single") || subject.IsSystem("Double");

    // System.Numerics fuses the multiplies of these where the build or the processor offers it. Its plain operators
    // stay exact, and so do a dot product, a length, a normalize and a turn by a quaternion. The product of two 4x4
    // matrices is the one operator that fuses.
    private static bool IsFusedVectorMath(in Subject subject, IMethodSymbol method)
    {
        if (!subject.In("System.Numerics") && !subject.In("System.Runtime.Intrinsics"))
        {
            return false;
        }

        return method.Name is "Lerp" or "MultiplyAddEstimate" or "TransformNormal" or "Reflect"
            || (method.Name == "Transform" && !method.Parameters.Any(static parameter => parameter.Type.Name == "Quaternion"))
            || (subject.Type?.Name == "Matrix4x4"
                && method.Name is "op_Multiply" or "Multiply"
                && method.Parameters.Length == 2
                && method.Parameters[1].Type.Name == "Matrix4x4");
    }

    private static string Replacement(in Subject subject, IMethodSymbol method) => method switch
    {
        { Name: "SinCos" } => "Call DeterministicMath.Sin and DeterministicMath.Cos",
        { Name: "Log", Parameters.Length: 2 } =>
            $"Call DeterministicMath.Log({method.Parameters[0].Name}) / DeterministicMath.Log({method.Parameters[1].Name})",
        { Name: "Lerp" } when subject.Type?.Name is "Vector2" or "Single" => "Call DeterministicMath.Lerp",
        _ when IsTranscendental(subject) => "Call DeterministicMath." + method.Name,
        _ => PlainMathFix,
    };

    private static bool IsExternalState(in Subject subject) =>
        subject.IsSystem("Console")
        || subject.IsSystem("Environment")
        || (subject.Type?.Name == "Process" && subject.In("System.Diagnostics"));

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static void Report(OperationAnalysisContext context, DiagnosticDescriptor rule, Location location, in Subject subject, string display) =>
        context.ReportDiagnostic(Fix(rule, subject) is { } fix
            ? Diagnostic.Create(rule, location, display, fix)
            : Diagnostic.Create(rule, location, display));

    // CAP102, CAP103 and CAP107 name the replacement for what they refuse in a second placeholder.
    private static string? Fix(DiagnosticDescriptor rule, in Subject subject) =>
        rule == Diagnostics.PlatformMath ? Replacement(subject, (IMethodSymbol)subject.Symbol)
        : rule == Diagnostics.ExternalIo ? subject.IsSystem("Console") && subject.Symbol.Name.StartsWith("Write", StringComparison.Ordinal) ? LogFix : subject.Under("System.IO") ? SaveFix : ShellFix
        : rule == Diagnostics.Concurrency ? Waits(subject) ? WaitFix : StepFix
        : null;

    private static bool Waits(in Subject subject) =>
        subject.In("System.Threading.Tasks")
        || subject.In("System.Timers")
        || (subject.In("System.Threading") && (subject.Type?.Name == "Timer" || (subject.Type?.Name == "Thread" && subject.Symbol.Name == "Sleep")));

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
