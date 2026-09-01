# Security Policy

## Reporting a vulnerability

- Report suspected vulnerabilities privately to **jagstudiosdev@gmail.com** — do not open a public issue.
- Include the affected package and version, what an attacker can do, and a minimal reproduction; a proof of concept or stack trace helps.

## Scope

Capsule is a game engine library. It has no network stack, no authentication, no persistence layer
and no privileged installer. What is in scope is what it does touch:

- **Scene and asset parsing.** `Capsule.Scenes` reads scene documents and `Capsule.Cli` reads Tiled
  authoring files. Malformed input must fail with a `SceneDocumentFormatException` from the scene
  document reader and a `TiledImportException` from the Tiled importer, never with memory
  corruption, an unbounded allocation, or code execution.
- **Paths written on a player's machine.** The crash log resolves a folder under the OS-local
  application data directory from a game-supplied name. A name that escapes that directory, or
  resolves to something other than what it reads as, is a vulnerability.
- **The build hooks and source generators** shipped in `JAG.Capsule.Build`, which run inside a
  consuming game's build.
- **The published `JAG.Capsule.*` packages themselves** — a package whose contents do not match
  this repository at the tagged commit.

Out of scope: MonoGame, the .NET runtime, and anything else upstream — report those to their own
maintainers. A game's own code and content are the game's responsibility; Capsule treats a game's
scenes and assets as trusted input authored by the game's developers, not as attacker-controlled
data.

## Supported versions

- Only the latest published version is supported.
- Fixes ship as new versions, never as patches to older ones; breaking changes may accompany a security fix if that is what fixing it takes.
