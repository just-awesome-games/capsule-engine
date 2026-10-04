using System.Collections.Immutable;
using System.Text;
using Capsule.Assets;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Keys every scene class, matches each shipped document to the class composing it and its baseScene,
// and reports each refusal.
internal static class SceneResolver
{
    internal static ScenePlan Resolve(SceneInputs inputs)
    {
        if (!inputs.IsLogicAssembly)
        {
            return default;
        }

        List<Diagnostic> diagnostics = [];

        // A baseScene can name any Scene-deriving class, so KeyedBaseScenes below keys every model.
        // Registration is narrower: only a class the describer found eligible at all.
        ImmutableArray<SceneModel> registrable = [.. inputs.Models.Items.Where(static model => model.Registrable)];

        List<(string? DocumentName, SceneModel Model)> sound = [];
        RegistryPass.ValidateAndOrder(
            diagnostics,
            registrable,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => Reported(model.Fault),
            model => Claim(diagnostics, sound, model, inputs.RootNamespace));

        // Scenes with no document sort first and never collide.
        List<(string? DocumentName, SceneModel Model)> registered = RegistryPass.RejectDuplicateKeys(
            diagnostics,
            sound,
            static (left, right) =>
            {
                int byDocument = string.CompareOrdinal(left.DocumentName ?? string.Empty, right.DocumentName ?? string.Empty);

                return byDocument != 0 ? byDocument : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.DocumentName,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.At,
            Diagnostics.DuplicateSceneDocumentName);

        HashSet<string> claimed = new(registered.Select(static entry => entry.DocumentName).OfType<string>(), StringComparer.Ordinal);
        Dictionary<string, SceneModel> baseScenes = KeyedBaseScenes(diagnostics, inputs.Models.Items, inputs.RootNamespace);

        List<(string DocumentName, GeneratedBase? Base)> unclaimed = [];
        foreach (SceneDocumentModel document in inputs.Documents.Items)
        {
            if (!claimed.Contains(document.Key))
            {
                unclaimed.Add((document.Key, ResolveBase(diagnostics, document, baseScenes)));
            }
        }

        unclaimed.Sort(static (left, right) => string.CompareOrdinal(left.DocumentName, right.DocumentName));

        // A class no document names gets an applier too, so a test composing it by class sets its members. The
        // engine's plain Scene gets one for every class that adds no member to it.
        IEnumerable<SceneModel> applied = inputs.Models.Items.Where(static model => model.Applied)
            .Concat(inputs.EngineScene is { } engine ? [engine] : [])
            .GroupBy(static model => model.QualifiedName, StringComparer.Ordinal)
            .Select(static models => models.First())
            .OrderBy(static model => model.QualifiedName, StringComparer.Ordinal);

        ScenePlan plan = new(
            Generates: true,
            new([.. registered.Select(static entry => new RegisteredScene(entry.DocumentName, entry.Model))]),
            new([.. unclaimed.Select(static entry => new DocumentOnlyScene(entry.DocumentName, entry.Base))]),
            new([.. applied]),
            default,
            default,
            new([.. diagnostics]));
        EquatableArray<KeyedObject> objects = ObjectRenderer.Keyed(
            plan.Applied.Items.SelectMany(static model => model.Objects.Items),
            inputs.RootNamespace,
            diagnostics);

        return plan with
        {
            Objects = objects,
            Lookups = EntityResolver.Lookups(
                plan.Applied.Items.SelectMany(static model => model.Authored).Concat(ObjectRenderer.Authored(objects)),
                new AssetTable(inputs.Assets.Items, inputs.Documents.Items)),
            Diagnostics = new([.. diagnostics]),
        };
    }

    private static DiagnosticDescriptor? Reported(SceneFault fault) => fault switch
    {
        SceneFault.TypeKeyRequiresContentConstructor => Diagnostics.TypeKeyRequiresContentConstructor,
        SceneFault.InaccessibleType => Diagnostics.InaccessibleRegisteredType,
        SceneFault.AmbiguousConstructors => Diagnostics.AmbiguousSceneConstructors,
        _ => null,
    };

    // The document a sound class composes. The key comes from where the type is declared, so it is not
    // settled until the assembly's root namespace is known.
    private static void Claim(
        List<Diagnostic> diagnostics,
        List<(string? DocumentName, SceneModel Model)> sound,
        SceneModel model,
        string rootNamespace)
    {
        if (!model.Documented)
        {
            sound.Add((null, model));

            return;
        }

        // An explicit claim is normalized like the document's path, so any spelling of the claim
        // meets the document at the key it ships under.
        string? documentName = model.Declared is { } declared
            ? Normalized(diagnostics, model, declared)
            : TypeNaming.DocumentKeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);

        if (documentName is null)
        {
            return;
        }

        if (AssetPaths.IsKey(documentName))
        {
            sound.Add((documentName, model));

            return;
        }

        diagnostics.Add(Diagnostic.Create(
            Diagnostics.UnsafeSceneDocumentName, model.At.Location(), model.DisplayName, documentName));
    }

    private static string? Normalized(List<Diagnostic> diagnostics, SceneModel model, string declared)
    {
        if (AssetPaths.NormalizeKey(declared, out string? rejected) is { } key)
        {
            return key;
        }

        diagnostics.Add(Diagnostic.Create(
            Diagnostics.UnnameableSceneDocumentSegment, model.At.Location(), model.DisplayName, rejected));

        return null;
    }

    // Every scene the assembly declares, by its baseScene key. Eligible classes claim keys first under the type key
    // rule and its refusals. An ineligible class then fills a free key so a baseScene naming it gets CAP028, not CAP030.
    private static Dictionary<string, SceneModel> KeyedBaseScenes(
        List<Diagnostic> diagnostics, ImmutableArray<SceneModel> models, string rootNamespace)
    {
        Dictionary<string, SceneModel> keyed = RegistryPass.Keyed(
            diagnostics, models.Where(static model => model.BaseFault == SceneFault.None), rootNamespace);

        List<SceneModel> ineligible = [.. models.Where(static model => model.BaseFault != SceneFault.None)];
        ineligible.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));
        foreach (SceneModel model in ineligible)
        {
            string key = model.Declared ?? TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);
            if (!keyed.ContainsKey(key))
            {
                keyed.Add(key, model);
            }
        }

        return keyed;
    }

    private static GeneratedBase? ResolveBase(
        List<Diagnostic> diagnostics, SceneDocumentModel document, Dictionary<string, SceneModel> baseScenes)
    {
        if (document.BaseScene is not { } key)
        {
            return null;
        }

        if (!baseScenes.TryGetValue(key, out SceneModel model))
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.UnclaimedSceneKey, Location.None, document.Key, "baseScene", key));

            return null;
        }

        if (model.BaseFault != SceneFault.None)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.InvalidBaseScene, model.At.Location(), model.DisplayName, key));

            return null;
        }

        return new GeneratedBase(GeneratedSceneClassName(document.Key), model);
    }

    // The internal sealed scene a document's baseScene generates, named from the document's own key.
    // Each segment's identifier follows a '_', which no identifier contains. Two keys then share a
    // name only when they share every segment's identifier, and the build refuses that pair when it
    // declares the keys on CapsuleAssets.Scenes.
    private static string GeneratedSceneClassName(string documentKey)
    {
        StringBuilder name = new("CapsuleGeneratedScene");
        foreach (string part in documentKey.Split('/'))
        {
            name.Append('_').Append(AssetPaths.ToIdentifier(part));
        }

        return name.ToString();
    }
}
