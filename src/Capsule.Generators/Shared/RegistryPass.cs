using System.Collections.Immutable;
using Capsule.Assets;
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
    /// </summary>
    /// <remarks>
    /// Sorted by key, since the collected order follows the compiler's syntax trees. An entry that
    /// claims no key never collides.
    /// </remarks>
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
    /// Every class by the key it claims, as <see cref="KeyOf"/> spells it: a subclass a member object's type key names,
    /// or a document's baseScene. The first by <see cref="DeclarationOrder"/> keeps a key and a second is CAP003.
    /// </summary>
    /// <remarks>
    /// A blank or unsafe key claims nothing. A partial class's second declaration is the same class.
    /// </remarks>
    internal static Dictionary<string, TModel> Keyed<TModel>(List<Diagnostic> diagnostics, IEnumerable<TModel> models, string rootNamespace)
        where TModel : IClaimingClass
    {
        List<TModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));

        Dictionary<string, TModel> keyed = new(StringComparer.Ordinal);
        foreach (TModel model in ordered)
        {
            if (Claimed(diagnostics, model, KeyOf(model, rootNamespace)) is not { } key)
            {
                continue;
            }

            if (keyed.TryGetValue(key, out TModel claimed))
            {
                if (claimed.QualifiedName != model.QualifiedName)
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.DuplicateTypeKey, model.At.Location(), claimed.DisplayName, model.DisplayName, key));
                }

                continue;
            }

            keyed.Add(key, model);
        }

        return keyed;
    }

    /// <summary>
    /// The key a class claims: its <c>[TypeKey]</c> normalized as a document's path is, or else the key its namespace and
    /// name spell.
    /// </summary>
    /// <remarks>
    /// A scene's claim on a document reads its <c>[TypeKey]</c> by the same rule. Any spelling of a key meets every
    /// reference at the one key the build declares.
    /// </remarks>
    /// <returns>The key, or null when a segment of its <c>[TypeKey]</c> names nothing.</returns>
    internal static string? KeyOf<TModel>(TModel model, string rootNamespace)
        where TModel : IClaimingClass =>
        model.Declared is { } declared
            ? AssetPaths.NormalizeKey(declared, out _)
            : TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);

    /// <summary>
    /// <paramref name="key"/> when <paramref name="model"/> may claim it, or null after reporting why not: CAP004 for a
    /// blank <c>[TypeKey]</c>, and CAP019 for one that is no key.
    /// </summary>
    internal static string? Claimed<TModel>(List<Diagnostic> diagnostics, TModel model, string? key)
        where TModel : IClaimingClass
    {
        if (string.IsNullOrWhiteSpace(model.Declared ?? key))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.BlankTypeKey, model.At.Location(), model.DisplayName));
            return null;
        }

        if (key is null || !AssetPaths.IsKey(key))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsafeTypeKey, model.At.Location(), model.DisplayName, key ?? model.Declared));
            return null;
        }

        return key;
    }
}
