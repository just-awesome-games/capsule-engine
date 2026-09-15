# Text

Capsule draws text from a bitmap font: a font baked to texture pages, with a glyph rectangle per codepoint. Nothing is rasterized at run time and the engine never opens an outline font; a run is laid out left to right, one glyph per codepoint, with the kerning the font declares — no shaping, no bidirectional layout, no distance fields.

A game authors its fonts under the logic project's `Assets/Fonts/`, in any directory shape:

| Extension | What it is |
| --- | --- |
| `.fnt` | A BMFont description, text flavour, unpacked. Its metrics, glyphs and kerning compile into the logic assembly as a `BitmapFont`; the file itself never ships. |
| `.png` | A page the description names. Ships under `assets/fonts/` at its own key. |

A font and its pages are keyed off their authored paths ([`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets)), so a page beside its font ships beside it; a description naming a page the game does not ship fails the build. `BitmapFont.Default` ships inside the runtime and needs no `Fonts/` asset.

`Label` puts a run of a font on an entity; `GlyphRun` is the layout pass every placement comes from, so a consumer emitting its own per-glyph sprites enumerates the same geometry the engine draws and measures.
