# Security Policy

## Reporting a vulnerability

- Report suspected vulnerabilities privately to **jagstudiosdev@gmail.com**. Do not open a public issue.
- Include the affected package and version, what an attacker can do, and a minimal reproduction. A proof of concept or stack trace helps.

## Scope

Capsule is a game engine library. It has no network stack, no authentication and no privileged
installer. What is in scope is what it does touch:

- **Scene and asset parsing.** `Capsule.Scenes` reads scene documents. Malformed input must fail
  with a `SceneDocumentFormatException` from the scene document reader, never with memory
  corruption, an unbounded allocation, or code execution.
- **Save files.** `Run.Saves` reads JSON that a player can edit or copy from another machine. A save
  file that does not parse must be set aside as corrupt, and a document that no longer fits the
  game's type must fail with a `JsonException` from `Read`. Neither may cause memory corruption, an
  unbounded allocation, or code execution.
- **Paths written on a player's machine.** The crash log and the saves resolve a folder under the
  OS-local application data directory from a game-supplied name, and each save takes its file name
  from its document's name. A name that escapes its directory, or resolves to something other than
  what it reads as, is a vulnerability.
- **The build hooks and source generators** shipped in `JAG.Capsule`, which run inside a
  consuming game's build.
- **The published `JAG.Capsule.*` packages themselves.** A package whose contents do not match
  this repository at the tagged commit is a vulnerability.

Out of scope: MonoGame, the .NET runtime, and anything else upstream. Report those to their own
maintainers. A game's own code and content are the game's responsibility. Capsule treats a game's
scenes and assets as trusted input authored by the game's developers, not as attacker-controlled
data.

## Supported versions

- Only the latest published version is supported.
- Fixes ship as new versions, never as patches to older ones. A security fix may carry a breaking change when fixing it takes one.
