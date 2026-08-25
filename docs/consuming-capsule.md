# Consuming Capsule

Everything a game repository does to build against this one, in the order it does it. Capsule
ships as source at a sibling path, so all of this lives in the client repo and none of it is a
packaging step.

## Bootstrap a new game

### 1. Clone the engine beside the game

```
Development/
  capsule-engine/
  my-game/
```

```
git clone https://github.com/just-awesome-games/capsule-engine.git
```

There is no feed, no package and no version number, so local development edits both repos inside
one build graph. Commit a `capsule-engine.pin` at the game repo root holding the git ref CI
clones the engine at: a branch name tracks the tip, a full commit SHA pins a build. It is bumped
in the same commit as the game change that needs the newer engine.

### 2. Lay out the repo

```
my-game/
  MyGame.sln
  MyGame.Shell/               # the host executable
    MyGame.Shell.csproj
    Program.cs
    Assets/                   # shipped content; Assets/Levels/ is derived, never committed
  MyGame.Game/                # game logic, substrate-free
    MyGame.Game.csproj
  MyGame.Tests/               # xUnit over MyGame.Game, run headless
  asset-sources/
    levels/                   # .tmj maps and their .tsj tilesets
  capsule-engine.pin
  Directory.Build.props
  Directory.Build.targets
  global.json
  .editorconfig
```

**The two projects are named for their roles.** `MyGame.Game` is the logic the purity rule below
holds substrate-free; `MyGame.Shell` is the host that owns the window, the device and file IO.
The shell sets `<AssemblyName>` to the bare game name so the shipped executable is `MyGame.exe`,
not `MyGame.Shell.exe`.

**`asset-sources/` is top-level, owned by no project.** Level sources are game content, not one
project's content: a second target shell feeds from the same maps, so putting them under a shell
would make one of them the odd owner of everything the others build from.

Capsule has no asset scanner to hide a file from, so what ships is exactly what the shell
`.csproj` copies. Authoring sources stay unshipped by living outside `Assets/`, never by a naming
convention.

**The skeleton is the ceremony bar.** A new Capsule game is a `Program.cs` shell plus its own
logic project, and an engine capability that would grow that list is misdesigned.

### 3. Take the build import

Build logic cannot travel through a `ProjectReference`, so the game imports it once. Copy
[`build/client/Directory.Build.targets`](../build/client/Directory.Build.targets) from the engine
clone to the game repo root, verbatim — it resolves the engine at the sibling path and fails with
a legible error when the clone is missing.

That import reaches every project in the repo, so every Capsule build hook, present and future,
arrives through it and a new hook is never new client wiring. Today it derives levels
([`Capsule.Levels/README.md`](../Capsule.Levels/README.md)).

### 4. Declare the shell

The import reaches every project, so the one that ships content says so:

```xml
<!-- MyGame.Shell.csproj -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <AssemblyName>MyGame</AssemblyName>
  <CapsuleGameShell>true</CapsuleGameShell>
</PropertyGroup>
```

The logic and test projects do not set it, and every hook stays inert there.

### 5. Wire the references

```xml
<!-- MyGame.Game.csproj -->
<ProjectReference Include="../../capsule-engine/Capsule.Core/Capsule.Core.csproj" />
<ProjectReference Include="../../capsule-engine/Capsule.Levels/Capsule.Levels.csproj" />

<!-- MyGame.Shell.csproj -->
<ProjectReference Include="../../capsule-engine/Capsule.Runtime/Capsule.Runtime.csproj" />
<ProjectReference Include="../MyGame.Game/MyGame.Game.csproj" />
```

### 6. Lock the restore

`Directory.Build.props` sets `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`,
every package-consuming project commits its own `packages.lock.json`, and CI restores with
`--locked-mode` so a restore can never silently move a version. Capsule is never a package and
never appears in a lock file.

### 7. Configure the engine

`Program.cs` reads the content the simulation starts from, then calls
`CapsuleEngine.Configure()`. Set only what you disagree with — every knob ships a default, and
restating one you agree with is noise.

## The purity rule

`MyGame.Game` references **only Capsule modules that carry no substrate reference** —
`Capsule.Core` and `Capsule.Levels` today — and never `Capsule.Runtime`. Being substrate-free is
the admission test rather than membership in a fixed list, so the set grows without the rule
weakening.

**File IO is the shell's.** A pure module supplies the parser and the model; the shell reads the
bytes and hands the parsed value to the simulation's constructor. Game logic therefore takes its
content already in hand and never touches a path, which is what keeps a logic test a pure
function of its inputs.

The project references hold that boundary mechanically, and `Capsule.Runtime` privatises its
backend's compile assets so no backend type reaches the shell either. The one gap is deliberate:
**a game repo never adds a backend `PackageReference` of its own.** Privatisation closes the
transitive path, not a direct one, and taking it is a defect rather than a compile error.
