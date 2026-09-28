using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>One entity class registered under the key it claims.</summary>
internal readonly record struct RegisteredEntity(string SpawnType, EntityModel Model);

/// <summary>The lookup one asset type an authored member takes resolves its keys through.</summary>
/// <param name="Assets">Each declared asset of the form, by key, with the member it is read from.</param>
internal readonly record struct AssetLookup(AssetForm Form, EquatableArray<(string Key, string Member)> Assets);

/// <summary>What <c>CapsuleEntities.g.cs</c> holds, resolved from every entity class and every faulted claim.</summary>
/// <param name="Generates">Whether the assembly gets the file at all: only a logic assembly does.</param>
/// <param name="Registered">Every sound class by key, one per key, a class only code can place included.</param>
/// <param name="ClaimedKeys">Every key any entity class claims, faulted or not, sorted.</param>
/// <param name="Lookups">The asset lookups the registered classes' authored members read through.</param>
/// <param name="Diagnostics">Every fault found resolving the plan.</param>
internal readonly record struct EntityPlan(
    bool Generates,
    EquatableArray<RegisteredEntity> Registered,
    EquatableArray<string> ClaimedKeys,
    EquatableArray<AssetLookup> Lookups,
    EquatableArray<Diagnostic> Diagnostics)
{
    /// <summary>Every class a scene document places, which the file registers and claims.</summary>
    internal IEnumerable<RegisteredEntity> Registrations => Registered.Items.Where(static entry => !entry.Model.CodeOnly);
}
