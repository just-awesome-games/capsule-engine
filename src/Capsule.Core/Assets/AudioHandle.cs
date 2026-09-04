namespace Capsule.Assets;

/// <summary>
/// Pure data naming <c>assets/audio/{Name}{Extension}</c> beside the executable. <c>Name</c> is the
/// source's path under the <c>audio</c> root with forward slashes and no extension, so a nested
/// asset keeps its directories: <c>enemies/bat</c> ships at <c>assets/audio/enemies/bat.ogg</c>.
/// </summary>
public readonly record struct AudioHandle(string Name, string Extension);
