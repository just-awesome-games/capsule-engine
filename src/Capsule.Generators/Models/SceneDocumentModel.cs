namespace Capsule.Generators;

/// <summary>
/// One scene document the build ships, keyed as the build keys it, with the two top-level fields
/// <see cref="SceneResolver"/> resolves before the document is otherwise read and the game entries
/// <see cref="PlacementCheck"/> checks against each claiming class.
/// </summary>
/// <param name="Source">The file an authoring module derived the document from, or null.</param>
/// <param name="Path">The file the build read, where an entry's error is reported, or null.</param>
/// <param name="Member">The fully qualified <c>CapsuleAssets</c> member holding the document's key.</param>
internal readonly record struct SceneDocumentModel(
    string Key,
    string Member,
    string? BaseScene,
    string? Camera,
    string? Source,
    string? Path,
    EquatableArray<PlacementModel> Placements);
