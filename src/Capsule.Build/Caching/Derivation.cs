using System.Reflection;

namespace Capsule.Build.Caching;

/// <summary>
/// What one derivation's result depends on: the files it reads, the settings it applies and the
/// assembly that implements it. <see cref="DerivationCache"/> stamps it from these alone.
/// </summary>
/// <param name="Name">What names it in the cache and in every report: its source's path, or an atlas's file.</param>
/// <param name="Inputs">Every file it reads, relative to the working directory as the walk spells each.</param>
/// <param name="Settings">Everything else its result depends on, as text.</param>
/// <param name="Tool">The assembly implementing it.</param>
internal sealed record Derivation(string Name, IReadOnlyList<string> Inputs, string Settings, Assembly Tool)
{
    /// <summary>The engine's own derivation of <paramref name="source"/> alone, which ships under its key.</summary>
    /// <param name="settings">What else the result depends on, as <c>format=r8</c>, or empty.</param>
    internal static Derivation Of(Source source, string settings = "") =>
        new(
            source.Path,
            [source.Path],
            settings.Length == 0 ? $"key={source.Key}{source.Extension}" : $"key={source.Key}{source.Extension}; {settings}",
            typeof(Derivation).Assembly);
}
