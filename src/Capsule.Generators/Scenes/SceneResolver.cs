using System.Collections.Immutable;
using System.Text;
using Capsule.Assets;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Keys every scene and camera class, matches each shipped document to the class composing it, its
// baseScene and its camera, and reports each refusal.
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
        Dictionary<string, CameraModel> cameras = KeyedCameras(diagnostics, inputs.Cameras.Items, inputs.RootNamespace);

        Dictionary<string, string?> cameraOf = new(StringComparer.Ordinal);
        List<(string DocumentName, GeneratedBase? Base)> unclaimed = [];
        foreach (SceneDocumentModel document in inputs.Documents.Items)
        {
            cameraOf[document.Key] = ResolveCamera(diagnostics, document, cameras);

            if (!claimed.Contains(document.Key))
            {
                unclaimed.Add((document.Key, ResolveBase(diagnostics, document, baseScenes)));
            }
        }

        unclaimed.Sort(static (left, right) => string.CompareOrdinal(left.DocumentName, right.DocumentName));

        return new ScenePlan(
            Generates: true,
            new([.. registered.Select(entry => new RegisteredScene(entry.DocumentName, entry.Model, CameraOf(entry.DocumentName)))]),
            new([.. unclaimed.Select(entry => new DocumentOnlyScene(entry.DocumentName, entry.Base, CameraOf(entry.DocumentName)))]),
            new([.. diagnostics]));

        string? CameraOf(string? documentName) =>
            documentName is not null && cameraOf.TryGetValue(documentName, out string? camera) ? camera : null;
    }

    private static DiagnosticDescriptor? Reported(SceneFault fault) => fault switch
    {
        SceneFault.SceneDocumentRequiresContentConstructor => Diagnostics.SceneDocumentRequiresContentConstructor,
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

    // Every scene the assembly declares, keyed the way a document-backed scene class is. Concrete
    // classes are modeled too, so a baseScene that names one resolves to a class CAP028 can name
    // instead of falling through to CAP030. A class that could actually serve as a base always wins
    // its key over a same-keyed one that never could, whichever declares first, so resolution never
    // depends on declaration order: an eligible pass claims every key it can before an ineligible one
    // is let fill what remains, purely so CAP028 still has a class to name. Two classes that could
    // *both* serve as a baseScene claiming one key is CAP032.
    private static Dictionary<string, SceneModel> KeyedBaseScenes(
        List<Diagnostic> diagnostics, ImmutableArray<SceneModel> models, string rootNamespace)
    {
        List<SceneModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));

        Dictionary<string, SceneModel> keyed = new(StringComparer.Ordinal);
        foreach (SceneModel model in ordered)
        {
            if (model.BaseFault != SceneFault.None)
            {
                continue;
            }

            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);
            if (keyed.TryGetValue(key, out SceneModel claimed))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.DuplicateBaseSceneKey, model.At.Location(), claimed.DisplayName, model.DisplayName, key));
                continue;
            }

            keyed.Add(key, model);
        }

        foreach (SceneModel model in ordered)
        {
            if (model.BaseFault == SceneFault.None)
            {
                continue;
            }

            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);
            if (!keyed.ContainsKey(key))
            {
                keyed.Add(key, model);
            }
        }

        return keyed;
    }

    // Every camera the assembly declares, by the key its namespace and name claim. The first class in
    // declaration order keeps a key, and a second claiming it is CAP031, the failure a room quietly
    // framed by the wrong camera would otherwise hide.
    private static Dictionary<string, CameraModel> KeyedCameras(
        List<Diagnostic> diagnostics, ImmutableArray<CameraModel> models, string rootNamespace)
    {
        List<CameraModel> ordered = new(models);
        ordered.Sort(static (left, right) =>
            DeclarationOrder.Compare(left.QualifiedName, left.At, right.QualifiedName, right.At));

        Dictionary<string, CameraModel> keyed = new(StringComparer.Ordinal);
        foreach (CameraModel model in ordered)
        {
            string key = TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace);
            if (keyed.TryGetValue(key, out CameraModel claimed))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.DuplicateCameraKey, model.At.Location(), claimed.DisplayName, model.DisplayName, key));
                continue;
            }

            keyed.Add(key, model);
        }

        return keyed;
    }

    // The camera class a document's camera key names, or null when it names none or one that cannot serve.
    private static string? ResolveCamera(List<Diagnostic> diagnostics, SceneDocumentModel document, Dictionary<string, CameraModel> cameras)
    {
        if (document.Camera is not { } key)
        {
            return null;
        }

        if (!cameras.TryGetValue(key, out CameraModel model))
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.UnclaimedSceneKey, Location.None, document.Key, "camera", key));

            return null;
        }

        if (!model.Valid)
        {
            diagnostics.Add(Diagnostic.Create(
                Diagnostics.InvalidCamera, model.At.Location(), model.DisplayName, key));

            return null;
        }

        return model.QualifiedName;
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

        return new GeneratedBase(GeneratedSceneClassName(document.Key), model.QualifiedName);
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
