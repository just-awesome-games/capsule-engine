using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Refuses an inaccessible class and a second class claiming a taken name.
internal static class NamedClassResolver
{
    // The first class by qualified name keeps a taken name.
    internal static NamedClassPlan Resolve(EquatableArray<NamedClassModel> models, NamedRegistry registry)
    {
        List<Diagnostic> diagnostics = [];
        List<NamedClassModel> sound = [];
        Dictionary<string, NamedClassModel> claimed = new(StringComparer.Ordinal);

        RegistryPass.ValidateAndOrder(
            diagnostics,
            models.Items,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => model.Accessible ? null : Diagnostics.InaccessibleRegisteredType,
            model =>
            {
                if (claimed.TryGetValue(model.TypeName, out NamedClassModel previous))
                {
                    diagnostics.Add(Diagnostic.Create(
                        registry.Duplicate,
                        model.At.Location(),
                        previous.DisplayName,
                        model.DisplayName,
                        model.TypeName));

                    return;
                }

                claimed.Add(model.TypeName, model);
                sound.Add(model);
            });

        return new NamedClassPlan(new([.. sound]), new([.. diagnostics]));
    }
}
