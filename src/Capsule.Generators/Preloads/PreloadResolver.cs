namespace Capsule.Generators;

// Joins the parts of each partial class.
internal static class PreloadResolver
{
    internal static PreloadPlan Resolve(EquatableArray<PreloadModel> models) =>
        new(new([.. models.Items
            .GroupBy(static model => model.TypeOfName, StringComparer.Ordinal)
            .OrderBy(static parts => parts.Key, StringComparer.Ordinal)
            .Select(static parts => new PreloadModel(
                parts.Key,
                new([.. parts.SelectMany(static part => part.Reads.Items)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static read => read, StringComparer.Ordinal)])))]));
}
