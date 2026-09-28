using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Generators;

/// <summary>The role a project's file declares, which decides what the generator and analyzers do in it.</summary>
internal enum GeneratorRole
{
    /// <summary>Neither role: an engine module, a test or a tool. Nothing is generated or checked.</summary>
    None,

    /// <summary><c>&lt;CapsuleGameLogic&gt;</c>: the game's registries are generated here.</summary>
    Logic,

    /// <summary><c>&lt;CapsuleGameShell&gt;</c>: the game's entry point is generated here.</summary>
    Shell,

    /// <summary>Both roles, which CAP011 refuses.</summary>
    Conflict,
}

internal static class GeneratorRoles
{
    internal static GeneratorRole Read(AnalyzerConfigOptions options)
    {
        bool logic = Declares(options, MetadataNames.LogicRoleProperty);
        bool shell = Declares(options, MetadataNames.ShellRoleProperty);

        return logic && shell ? GeneratorRole.Conflict
            : logic ? GeneratorRole.Logic
            : shell ? GeneratorRole.Shell
            : GeneratorRole.None;
    }

    /// <summary>Whether the logic boundary applies: the logic role, alone or in conflict with the shell role.</summary>
    internal static bool DeclaresLogic(this GeneratorRole role) => role is GeneratorRole.Logic or GeneratorRole.Conflict;

    // MSBuild passes a boolean property through verbatim, so compare it case-insensitively.
    private static bool Declares(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
