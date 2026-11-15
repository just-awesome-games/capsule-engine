using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Tests.Generators;

internal static partial class GeneratorHarness
{
    /// <summary>The roles a project declares, as the compiler hands them to a generator or analyzer.</summary>
    internal sealed class DeclaredRole : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Properties(logic: false, shell: false, rootNamespace: null);

        public override AnalyzerConfigOptions GlobalOptions { get; }

        internal DeclaredRole(bool logic, bool shell, string? rootNamespace = null) =>
            GlobalOptions = new Properties(logic, shell, rootNamespace);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;

        private sealed class Properties(bool logic, bool shell, string? rootNamespace) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                bool declared = logic && string.Equals(key, "build_property.CapsuleGameLogic", StringComparison.Ordinal)
                    || shell && string.Equals(key, "build_property.CapsuleGameShell", StringComparison.Ordinal);
                if (declared)
                {
                    value = "true";

                    return true;
                }

                if (rootNamespace is not null && string.Equals(key, "build_property.RootNamespace", StringComparison.Ordinal))
                {
                    value = rootNamespace;

                    return true;
                }

                value = null;

                return false;
            }
        }
    }
}
