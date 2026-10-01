using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Refuses an inaccessible driver and a second driver claiming a taken name.
internal static class InputDriverResolver
{
    // The first class by qualified name keeps a taken name.
    internal static InputDriverPlan Resolve(EquatableArray<InputDriverModel> models)
    {
        List<Diagnostic> diagnostics = [];
        List<InputDriverModel> sound = [];
        Dictionary<string, InputDriverModel> claimed = new(StringComparer.Ordinal);

        RegistryPass.ValidateAndOrder(
            diagnostics,
            models.Items,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => model.Accessible ? null : Diagnostics.InaccessibleRegisteredType,
            model =>
            {
                if (claimed.TryGetValue(model.TypeName, out InputDriverModel previous))
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.DuplicateInputDriverName,
                        model.At.Location(),
                        previous.DisplayName,
                        model.DisplayName,
                        model.TypeName));

                    return;
                }

                claimed.Add(model.TypeName, model);
                sound.Add(model);
            });

        return new InputDriverPlan(new([.. sound]), new([.. diagnostics]));
    }
}
