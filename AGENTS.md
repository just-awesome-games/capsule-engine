# Agents

Operating rules for anyone changing this repository. They are short because every one of them
is a rule the compiler, a test or CI cannot already enforce for you.

Read [`README.md`](README.md) first for what Capsule is, and
[`docs/architecture.md`](docs/architecture.md) for how the pieces fit.

## Before you edit a module

Read that module's `README.md` — [`Capsule.Core`](Capsule.Core/README.md),
[`Capsule.Runtime`](Capsule.Runtime/README.md) — and **update it in the same change** when the
change alters a contract it describes. A module README that lags its module is worse than none:
it is confidently wrong.

## The boundaries

**`Capsule.Core` never references MonoGame, or anything else.** It has no package references at
all, so this is compiler-enforced rather than reviewed. Do not add one. Logic that needs a
device belongs in `Capsule.Runtime`.

**No MonoGame type appears in any public or protected signature of `Capsule.Runtime`** — not as
a parameter, return type, property, field, base type or generic argument. Public API speaks
Capsule types and BCL types only. `Capsule.Tests`'s public-surface guard fails the build's test
gate if one slips through, and the csproj's `PrivateAssets` means a consuming game could not
name the type anyway. Both halves are load-bearing; neither is decoration.

**A backend type crossing into a public signature is the failure mode that matters.** It is how
an engine stops being swappable, and it happens one convenient `Vector2` at a time.

## Placement — no engine code without a call site

**Engine code lands only alongside a consuming game call site in the same change-set.** A
subsystem with no caller is a guess about a future need, and it calcifies before anything
corrects it. If a game does not need it yet, the direction may be recorded in
[`docs/architecture.md`](docs/architecture.md) — the code may not exist.

The corollary: what a game *can* express should be as small as the game needs. `FrameView` has
no members at all because nothing in a game draws yet; render intent lands one member at a
time, each with the call site that needs it. Filling it in now would be a pile of decisions
made without evidence.

## Comments and doc-comments

Match the density of the file you are in. A comment earns its place only by stating something
the code cannot: an invariant, a unit, a hazard, a non-obvious why. Delete anything that
narrates the next line, re-explains a design decision at essay length, or addresses a reviewer.
Doc-comments go on public API, at the length the neighbouring members use.

A technical fact lives in the strongest home available: compiler-enforced structure first, then
a test or assert, then a one-line comment at the site. Prose is the last resort.

## Tests

Everything in `Capsule.Core` is pure and therefore testable; specs ship in the same change as
the code, and CI gates line coverage over `Capsule.Core` at 80%. `Capsule.Runtime` is tested
where it is headless — builder validation, the public-surface guard — and its window-and-device
paths are covered by a consuming game's verify run, not by a mock.

Never assert on a game's tuning values or authored content. Tests assert engine behaviour.

## The studio standard

The binding MonoGame standard lives in the design repo at
`studio/technical/engines/monogame/best-practices.md` and governs this repository: the
logic/shell split, warnings-as-errors, the studio C# dialect, hot-path allocation discipline,
the fixed-timestep mandate, locked restores, and the CI gate set. Read it before changing
project files, CI, or anything in the fixed step. A deviation from it is recorded, never
silent.
