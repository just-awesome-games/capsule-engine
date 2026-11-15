namespace Capsule.Generators;

/// <summary>Every class whose own code reads a preloaded <c>CapsuleAssets</c> member, with its parts joined.</summary>
/// <param name="Classes">One model per class by <c>typeof</c> name, its reads sorted and each once.</param>
internal readonly record struct PreloadPlan(EquatableArray<PreloadModel> Classes);
