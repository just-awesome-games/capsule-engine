namespace Capsule.Build.Shaders;

/// <summary>One compiler diagnostic, anchored where the compiler placed it.</summary>
/// <param name="File">The file the compiler named, as the composed source's line directive spelled it.</param>
/// <param name="Line">The one-based line in that file.</param>
/// <param name="Column">The one-based column in that line.</param>
/// <param name="Warning">Whether it is a warning, which fails nothing.</param>
/// <param name="Message">What the compiler said.</param>
internal readonly record struct ShaderDiagnostic(string File, int Line, int Column, bool Warning, string Message)
{
    /// <summary>Where the diagnostic is, in MSBuild's canonical form, as <c>fx/glow.fx(3,26)</c>.</summary>
    internal string Anchor => $"{File.Replace('\\', '/')}({Line},{Column})";

    /// <summary>What the diagnostic says after its anchor, as MSBuild's canonical form spells it.</summary>
    internal string Report => $"{(Warning ? "warning" : "error")} : {Message}";

    /// <summary>MSBuild's canonical form, which the build reports against the file and line.</summary>
    public override string ToString() => $"{Anchor}: {Report}";
}
