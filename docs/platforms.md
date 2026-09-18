# Platforms

A host family is one shell: the desktop shell publishes Windows, Linux and macOS from one project by runtime identifier; a console is another family with a shell of its own. A platform module is the assembly that tells the host what the family is — where shipped content is read from, where saves and the crash log land, how the window is raised, focused and redrawn, and how sound follows the default output — as one subclass of `HostPlatform`, which the shell hands `CapsuleBoot.Configure` beside the game's name. `Capsule.Runtime` is the platform-neutral host: it holds no implicit location and no native binding, and the compiler refuses one (`src/Capsule.Runtime/BannedSymbols.txt`).

## The shipped module

`Capsule.Runtime.Desktop` is the platform module the engine ships, and the only one: `DesktopPlatform` reads content beside the executable, keeps saves and `crash.log` in the per-user local folder ([`persistence.md`](persistence.md)), raises and claims the window through the windowing library, keeps drawing through a modal resize, and follows the operating system's default audio output. It is an ordinary consumer of the neutral host's public surface — the runtime grants it no internals — so it is also the proof that a private module can be written against the contract.

Three packages ship for a game: `JAG.Capsule` (logic), `JAG.Capsule.Runtime.Desktop` (the shell's reference, which brings `JAG.Capsule.Runtime` and the substrate with it), and `JAG.Capsule.Build` (build tooling). The reference graph is shell → platform module → `Capsule.Runtime` → the pure modules; game logic references none of the runtime.

## A private platform module

A console runtime is a private repository — one per platform, holding whatever its NDA covers — that is never a branch of the engine:

1. Subclass `HostPlatform` there. The two abstract members, `OpenContent` and `OpenSaveStorage`, are the whole of what a headless run needs; every window and audio member has a no-window default the module overrides where the platform has a policy.
2. Put the game's shell for that host family in the same repository, referencing the module and passing it to `CapsuleBoot.Configure`. The logic project is untouched.
3. Consume Capsule in source mode at a pinned tag (`CapsuleSourcePath`, [`consuming-capsule.md`](consuming-capsule.md#package-and-source-modes)) and swap the substrate with `CapsuleSubstratePackage` and `CapsuleSubstrateVersion`, the two properties `src/Capsule.Runtime/Capsule.Runtime.csproj` reads; an unmodified checkout retargets.

Isolation at publish is the reference graph: a shell references exactly one platform module, so a desktop assembly is absent from a console publish rather than trimmed from it. The engine's CI publishes and runs the desktop path only; a private module is tested by its own repository.
