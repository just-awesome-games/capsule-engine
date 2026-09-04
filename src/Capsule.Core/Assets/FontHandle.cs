namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/fonts/{Name}{Extension}</c> beside the executable. <c>Name</c> is the
/// source's path under the <c>fonts</c> root with forward slashes and no extension, so a nested
/// asset keeps its directories: <c>body/serif</c> ships at <c>assets/fonts/body/serif.ttf</c>.
/// </summary>
public readonly record struct FontHandle(string Name, string Extension);
