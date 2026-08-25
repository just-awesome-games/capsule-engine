# Agents

Operating rules for anyone changing this repository. They are short because every one of them
is a rule the compiler, a test or CI cannot already enforce for you.

## Code is the documentation plane

Agents reason through code, not prose. **The whole doc surface is
[`README.md`](README.md), [`docs/architecture.md`](docs/architecture.md),
[`docs/consuming-capsule.md`](docs/consuming-capsule.md),
[`Capsule.Levels/README.md`](Capsule.Levels/README.md) and the doc-comments.** Nothing else.
A new prose document needs a ratified reason in the design repo's technical decision ledger
before it exists, and the two beyond the root pair each earn theirs the same way — by carrying
knowledge that lives outside this repository. `consuming-capsule.md` describes a client repo's
own files, which no code here compiles; `Capsule.Levels/README.md` covers installing and driving
a third-party editor. Module READMEs are not a general permission: a module whose story is told
by its API gets none.

**Doc-comments on the public API are the API reference.** They are API-specific — contract,
invariant, unit, hazard — and nothing else. No history (what a thing replaced, regained or used
to be), no forward reference to a feature that has not landed, no design-rationale essay:
rationale belongs in the ledger. Any extra colour is one tight clause, or it is cut. Every other
comment earns its place the same way, at the density of the file it is in; delete anything that
narrates the next line or addresses a reviewer.

**Prose that restates code is deleted on sight**, wherever it is found. The root pair earns its
place the same way: `README.md` is human-first orientation — what Capsule is, its shape, a
quickstart, how to build and test — and `docs/architecture.md` is the determinism contract plus
the capabilities designed but not yet built.

A technical fact lives in the strongest home available: compiler-enforced structure first, then
a test or assert, then a one-line comment at the site. Prose is the last resort.

Whatever survives, **update it in the same change** as the code it describes. Documentation that
lags is worse than none: it is confidently wrong.

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

The corollary: what a game *can* express should be as small as the game needs. `FrameView`
carries a camera and quads because that is what a game draws; render intent lands one member at
a time, each with the call site that needs it. Filling it in ahead of that would be a pile of
decisions made without evidence.

## Publishable as-is

**Every commit should be able to go public unchanged.** No game vocabulary anywhere on the
public surface, in-repo docs that stand without studio context, CI a stranger could run, every
public member defensible in public.

This is a quality bar, never a scope bar. It disciplines how a capability lands; it never
argues for building one. A generalisation, hook or option for a hypothetical external user is
still speculative engine and still fails the placement rule above. Open-sourcing this
repository is not a commitment.

## Tests

Everything in `Capsule.Core` is pure and therefore testable; specs ship in the same change as
the code, and CI gates line coverage over `Capsule.Core` at 80%. `Capsule.Runtime` is tested
where it is headless — builder validation, deadzone filtering, the public-surface guard — and its
window-and-device paths are covered by a consuming game's verify run, not by a mock.

Never assert on a game's tuning values or authored content. Tests assert engine behaviour.

**A test is held to the comment bar: it must guard what the code cannot state for itself** — a
contract, an invariant, a boundary or a hazard. A test that restates what the code visibly does —
a property echo, a proof that the BCL works, one more permutation of an equality already covered —
is deleted like any other restatement. The coverage gate is a floor, never a target: no test
exists to move that number, and reaching it is not a reason to write one.

## Where the conventions live

**This repository is the authority on how Capsule is built and used.** MonoGame is a substrate
Capsule hides, so substrate conventions are Capsule's own and are held here, not deferred to
anything upstream:

- `Directory.Build.props` — nullable, warnings-as-errors, the analyzer level, lock files. A
  warning is fixed, or suppressed with a justification at the suppression site.
- [`docs/architecture.md`](docs/architecture.md) — the determinism contract, and the fixed step
  every gameplay path runs inside.
- [`docs/consuming-capsule.md`](docs/consuming-capsule.md) — the logic/shell split as it binds a
  consuming game, and the locked restore.
- [`README.md`](README.md) — the gates every change clears.

Read the relevant one before changing project files, CI, or anything in the fixed step. A
deviation is recorded in the same change, never silent.
