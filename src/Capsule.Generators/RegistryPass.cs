using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// The pass every declaration-backed registry runs: one model per declared type in a stable order,
// faults reported against the declaration, then the keys they claim held to one claimant each.
internal static class RegistryPass
{
    /// <summary>
    /// Hands <paramref name="resolve"/> every sound model once, in declaration order, and reports
    /// the diagnostic <paramref name="reported"/> names for the rest.
    /// </summary>
    internal static void Sound<TModel>(
        SourceProductionContext context,
        ImmutableArray<TModel> models,
        Func<TModel, string> qualifiedName,
        Func<TModel, string> displayName,
        Func<TModel, Location> location,
        Func<TModel, DiagnosticDescriptor?> reported,
        Action<TModel> resolve)
    {
        List<TModel> ordered = new(models);
        ordered.Sort((left, right) =>
            DeclarationOrder.Compare(qualifiedName(left), location(left), qualifiedName(right), location(right)));

        HashSet<string> described = new(StringComparer.Ordinal);
        foreach (TModel model in ordered)
        {
            // The parts of a partial class are one type, registered or faulted once.
            if (!described.Add(qualifiedName(model)))
            {
                continue;
            }

            if (reported(model) is { } descriptor)
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location(model), displayName(model)));
                continue;
            }

            resolve(model);
        }
    }

    /// <summary>
    /// Everything that claimed a key of its own, with a second claimant of one key refused against
    /// the first. Sorted by the key first: the collected order is whichever syntax trees the
    /// compiler handed over, and an entry claiming no key never collides.
    /// </summary>
    internal static List<TEntry> Claimed<TEntry>(
        SourceProductionContext context,
        List<TEntry> sound,
        Comparison<TEntry> order,
        Func<TEntry, string?> key,
        Func<TEntry, string> displayName,
        Func<TEntry, Location> location,
        DiagnosticDescriptor duplicate)
    {
        sound.Sort(order);

        List<TEntry> registered = new(sound.Count);
        foreach (TEntry entry in sound)
        {
            if (key(entry) is { } claimed
                && registered.Count > 0
                && string.Equals(key(registered[registered.Count - 1]), claimed, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    duplicate,
                    location(entry),
                    displayName(registered[registered.Count - 1]),
                    displayName(entry),
                    claimed));
                continue;
            }

            registered.Add(entry);
        }

        return registered;
    }
}
