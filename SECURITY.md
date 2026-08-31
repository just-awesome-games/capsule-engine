# Security Policy

## Reporting a vulnerability

Report suspected vulnerabilities privately to **jagstudiosdev@gmail.com**. Do not open a public
issue for one.

Include what you have: the affected package and version, what an attacker can do, and the
smallest reproduction you can manage. A proof of concept helps; a stack trace and a description
are enough to start.

Capsule is a pre-1.0 engine maintained by a small studio alongside its own games, and this policy
promises no acknowledgement, no response time and no disposition. Reports are read and handled on
a best-effort basis, and a serious one affecting a shipped package is what gets attention first.
If a report does lead to a fix, credit is offered to the reporter unless you would rather stay
anonymous.

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

Capsule is pre-1.0. Only the latest published version is supported, and a fix ships as a new
version rather than as a patch to an older one. There are no long-term support branches, and a
security fix may arrive alongside a breaking change if that is what fixing it takes.
