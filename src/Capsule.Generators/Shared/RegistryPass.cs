using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// The pass every declaration-backed registry runs. One model per declared type in a stable order,
// faults reported against the declaration, then one claimant per claimed key.
internal static class RegistryPass
{
    /// <summary>
    /// Hands <paramref name="resolve"/> every sound model once, in <see cref="DeclarationOrder"/>, and reports
    /// the diagnostic <paramref name="reported"/> names for the faulted ones.
    /// </summary>
    internal static void ValidateAndOrder<TModel>(
        List<Diagnostic> diagnostics,
        ImmutableArray<TModel> models,
        Func<TModel, string> qualifiedName,
        Func<TModel, string> displayName,
        Func<TModel, DeclaredAt> at,
        Func<TModel, DiagnosticDescriptor?> reported,
        Action<TModel> resolve)
    {
        List<TModel> ordered = new(models);
        ordered.Sort((left, right) =>
            DeclarationOrder.Compare(qualifiedName(left), at(left), qualifiedName(right), at(right)));

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
                diagnostics.Add(Diagnostic.Create(descriptor, at(model).Location(), displayName(model)));
                continue;
            }

            resolve(model);
        }
    }

    /// <summary>
    /// Everything that claimed a key, with a second claimant of a key refused against the first.
    /// Sorted by key, since the collected order follows the compiler's syntax trees. An entry that
    /// claims no key never collides.
    /// </summary>
    internal static List<TEntry> RejectDuplicateKeys<TEntry>(
        List<Diagnostic> diagnostics,
        List<TEntry> sound,
        Comparison<TEntry> order,
        Func<TEntry, string?> key,
        Func<TEntry, string> displayName,
        Func<TEntry, DeclaredAt> at,
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
                diagnostics.Add(Diagnostic.Create(
                    duplicate,
                    at(entry).Location(),
                    displayName(registered[registered.Count - 1]),
                    displayName(entry),
                    claimed));
                continue;
            }

            registered.Add(entry);
        }

        return registered;
    }

    /// <summary>
    /// Every class by the key its namespace and name claim, with no attribute to override it: a subclass a member
    /// object's type key names. The first by <see cref="DeclarationOrder"/> keeps a key and a
    /// second is CAP031. A partial class's second declaration is the same class.
    /// </summary>
    internal static Dictionary<string, TModel> Keyed<TModel>(
        List<Diagnostic> diagnostics, IEnumerable<TModel> models, string rootNamespace, string kind)
        where TModel : IClaimingClass
    {
        List<TModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));

        Dictionary<string, TModel> keyed = new(StringComparer.Ordinal);
        foreach (TModel model in ordered)
        {
            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);
            if (keyed.TryGetValue(key, out TModel claimed))
            {
                if (claimed.QualifiedName != model.QualifiedName)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.DuplicateClaimedKey, model.At.Location(), claimed.DisplayName, model.DisplayName, key, kind));
                }

                continue;
            }

            keyed.Add(key, model);
        }

        return keyed;
    }
}
