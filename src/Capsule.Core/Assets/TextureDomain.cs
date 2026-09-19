namespace Capsule.Assets;

// Which shipped root a texture's file sits under. A bitmap font's pages ship beside the font they were
// cut for. A handle carries where it resolves instead of the runtime guessing from a name.
internal enum TextureDomain
{
    Textures,
    Fonts,

    // The engine's own textures, which ship in no directory. The host holds them, so nothing is located,
    // loaded or preloaded for one.
    Engine,
}
