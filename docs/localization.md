# Localization

After this page you can show a game's text in more than one language and let the player switch it. Each
type's XML docs hold its rules.

## Texts

A string table is one language's texts, a `<tag>.strings.json` anywhere under `Assets/`. Nested objects
group keys, and the file's folder adds nothing to them:

```json
{
  "menu": { "begin": "Begin" },
  "options": { "windowScale": "Window: {scale}x" },
  "hud": { "bolts": "{count, plural, =0 {No bolts} one {# bolt} other {# bolts}}" }
}
```

Saved as `en.strings.json`, it declares `CapsuleStrings.Menu.Begin`, `CapsuleStrings.Options.WindowScale`
and `CapsuleStrings.Hud.Bolts`, each a `TextKey`. A label given a key shows it in the run's language:

```csharp
Add(new Label(font, CapsuleStrings.Menu.Begin));
_scale.SetText(CapsuleStrings.Options.WindowScale, scale);
```

`{scale}` fills from the argument, formatted once with the invariant culture. A text names at most three
placeholders, and `'{'` and `'}'` draw braces. A scene document names a key by its path, `"menu.begin"`.
A plural picks its form by exact count, else by the language's CLDR category, and `#` shows the count.
Its argument is an integer.

A placeholder takes another text when it is passed that text's key, as
`label.SetText(CapsuleStrings.Options.Speed, CapsuleStrings.Options.Fast)` for `"Speed: {speed}"`. The
inner text follows the language as the outer one does. It takes no arguments of its own and has one form.
A word whose form changes with its place in the sentence needs a whole text for each place.

`Run.GetText` returns a text as a string in the active language, taking the arguments a label takes, for a
log line, a platform call or a test. The string keeps that language after a switch. Text on screen belongs
in a label, which follows the switch and allocates nothing.

An array of texts is a list, as `"speech": ["Hello.", "Welcome back."]`. It declares a `TextList`,
whose `Count` texts are each a `TextKey`, shown as `label.SetText(speech[i])`. Its texts are keyed by position
from `speech.0`, and a translation gives the source's count. A text inserted in the source moves every later
one's key, in every translation.

## Languages

Each language is another file with the same keys, as `de.strings.json` or `pt-BR.strings.json`, spelled as
.NET spells the tag. The file ships its language, and no list names it. Several files may share a tag, and
their keys merge. A translation may move a placeholder, and it keeps its name. Its plurals give every form its
language uses for counts up to 999.

Only the source language, `en` unless the build project calls `WithSourceLanguage`, declares keys. The
build fails on a key the source lacks, a different set of placeholders, or a key defined twice. A key a
translation lacks shows the source text with a warning in a development build and fails a shipping one.

Text compiles into the game's logic assembly, and a lookup reads no file and allocates nothing. Each megabyte
of text, counted over every language, adds about 1.4 s to a rebuild and 1.7 MB to the assembly.

## Switching

`Run.Language` takes a BCP 47 tag and shows the closest shipped language, else the fallback, which is the
source language unless the build project calls `WithFallbackLanguage`. A played run
starts in the one closest to the player's system preference, and a headless or driven run in the fallback.
The getter returns the request at once for the save ([`persistence.md`](persistence.md)) that keeps it. A
saved choice applied in the `EngineBuilder.WithRunStart` hook wins and shows from the first step:

```csharp
if (settings.Language is { } chosen) { run.Language = chosen; }
```

A later request shows from the next step. `CapsuleStrings.Languages` lists the shipped tags for an options
row that cycles them. `--language <tag>` starts a development build in one, over a saved choice.
