namespace Capsule.Runtime;

/// <summary>
/// Thrown by <see cref="EngineBuilder.WithCommandLine"/> for a command line the engine will not run,
/// and for one that asked for the usage block. A shell catches it around its configuration chain and
/// returns what <see cref="Report"/> returns.
/// </summary>
/// <example>
/// <code>
/// try
/// {
///     return CapsuleBoot.Configure("My Game", new DesktopPlatform())
///         .WithCommandLine(args)
///         .RunScene&lt;TitleScene&gt;();
/// }
/// catch (CommandLineException failure)
/// {
///     return failure.Report();
/// }
/// </code>
/// </example>
public sealed class CommandLineException : Exception
{
    internal CommandLineException(string message, string usage, bool helpRequested)
        : base(message)
    {
        Usage = usage;
        HelpRequested = helpRequested;
    }

    /// <summary>The usage block for this game's standard command line, as <c>--help</c> prints it.</summary>
    public string Usage { get; }

    /// <summary>Whether the command line asked for the usage block instead of naming a defect.</summary>
    public bool HelpRequested { get; }

    /// <summary>The process's exit code: 0 for <c>--help</c>, 2 for a command line the engine refused.</summary>
    public int ExitCode => HelpRequested ? 0 : 2;

    /// <summary>
    /// Prints the usage block to standard output for <c>--help</c>, or the defect and the usage block
    /// to standard error, and returns <see cref="ExitCode"/>.
    /// </summary>
    public int Report()
    {
        if (HelpRequested)
        {
            Console.Out.WriteLine(Usage);

            return ExitCode;
        }

        Console.Error.WriteLine(Message);
        Console.Error.WriteLine(Usage);

        return ExitCode;
    }
}
