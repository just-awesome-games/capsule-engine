namespace Capsule.Generators;

/// <summary>One texture or sound the build declared on <c>CapsuleAssets</c>, as its <c>CapsuleGeneratedAsset</c> attribute marks it.</summary>
/// <param name="Type">The <see cref="AssetForm.Type"/> of its entry in <see cref="PropertyForms.Assets"/>.</param>
/// <param name="Key">Its key and extension, as a document names it once normalized: <c>textures/hazard.png</c>.</param>
/// <param name="Member">The fully qualified member generated code reads it from.</param>
internal readonly record struct AssetModel(string Type, string Key, string Member);
