# Text

Capsule draws text from a bitmap font: a font baked to texture pages, with a glyph rectangle per codepoint. Nothing is rasterized at run time, and the engine never opens an outline font.

A game authors its fonts under the logic project's `Assets/Fonts/`, in any directory shape it likes. That root admits two extensions:

| Extension | What it is |
| --- | --- |
| `.fnt` | A BMFont description, text flavour, unpacked. Its metrics, glyphs and kerning compile into the logic assembly as a `BitmapFont`; the file itself never ships. |
| `.png` | A page the description names. Ships under `assets/fonts/` at its own key. |

A font and its pages are keyed off their own authored paths, normalized as [`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets) defines, so a page beside its font ships beside it. A description naming a page the game does not ship fails the build.

A `Label` is the renderer component that puts a run of a font on an entity, laid out in a box with a size, word wrap, horizontal and vertical alignment, and a count of leading codepoints to reveal. `GlyphRun` is the public layout pass every placement comes from, so a consumer that emits its own per-glyph sprites enumerates the same geometry the engine draws and measures.

Not shipped: channel-packed pages, outline fonts loaded at run time, distance-field glyphs, text shaping, and right-to-left or bidirectional layout. A run is laid out left to right, one glyph per codepoint, with the kerning the font declares.
