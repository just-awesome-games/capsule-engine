namespace Capsule.Generators;

/// <summary>
/// One scene document the build ships, keyed as the build keys it, with the baseScene <see cref="SceneResolver"/>
/// resolves.
/// </summary>
internal readonly record struct SceneDocumentModel(string Key, string? BaseScene);
