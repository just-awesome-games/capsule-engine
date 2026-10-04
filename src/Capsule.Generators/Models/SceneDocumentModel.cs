namespace Capsule.Generators;

/// <summary>
/// One scene document the build ships, keyed as the build keys it, with the baseScene <see cref="SceneResolver"/>
/// resolves.
/// </summary>
/// <param name="Member">The fully qualified <c>CapsuleAssets</c> member holding the document's key.</param>
internal readonly record struct SceneDocumentModel(
    string Key,
    string Member,
    string? BaseScene);
