namespace Capsule.Assets;

// Which shipped root a texture's file sits under. A bitmap font's pages ship beside the font they
// were cut for, so a handle carries where it resolves rather than the runtime guessing from a name.
internal enum TextureDomain
{
    Textures,
    Fonts,

    // The engine's own, which ship in no directory at all: the host holds them, so nothing is
    // located, loaded or preloaded for one.
    Engine,
}
