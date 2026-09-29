namespace Capsule.Generators;

/// <summary>
/// One scene document the build ships, keyed as the build keys it, with the two top-level fields
/// <see cref="SceneResolver"/> resolves before the document is otherwise read, and the game entries and own
/// properties <see cref="PlacementCheck"/> checks against each claiming class.
/// </summary>
/// <param name="Source">The file an authoring module derived the document from, or null.</param>
/// <param name="Path">The file the build read, where an entry's error is reported, or null.</param>
/// <param name="Member">The fully qualified <c>CapsuleAssets</c> member holding the document's key.</param>
/// <param name="Properties">
/// The document's own authored properties, each in the form <see cref="PlacementModel.Properties"/> carries.
/// </param>
/// <param name="Line">The line the document's properties start on in its file, from 1, or 0 when unknown.</param>
/// <param name="Column">The column the document's properties start at, from 1, or 0 when unknown.</param>
internal readonly record struct SceneDocumentModel(
    string Key,
    string Member,
    string? BaseScene,
    string? Camera,
    string? Source,
    string? Path,
    EquatableArray<PlacementModel> Placements,
    EquatableArray<(string Name, object? Value)> Properties,
    int Line,
    int Column);
