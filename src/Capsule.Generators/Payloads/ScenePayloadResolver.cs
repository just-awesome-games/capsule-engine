using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Refuses an inaccessible payload and a second payload claiming a taken name.
internal static class ScenePayloadResolver
{
    // The first class by qualified name keeps a taken name.
    internal static ScenePayloadPlan Resolve(EquatableArray<ScenePayloadModel> models)
    {
        List<Diagnostic> diagnostics = [];
        List<ScenePayloadModel> sound = [];
        Dictionary<string, ScenePayloadModel> claimed = new(StringComparer.Ordinal);

        RegistryPass.ValidateAndOrder(
            diagnostics,
            models.Items,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => model.Accessible ? null : Diagnostics.InaccessibleRegisteredType,
            model =>
            {
                if (claimed.TryGetValue(model.TypeName, out ScenePayloadModel previous))
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.DuplicateScenePayloadName,
                        model.At.Location(),
                        previous.DisplayName,
                        model.DisplayName,
                        model.TypeName));

                    return;
                }

                claimed.Add(model.TypeName, model);
                sound.Add(model);
            });

        return new ScenePayloadPlan(new([.. sound]), new([.. diagnostics]));
    }
}
