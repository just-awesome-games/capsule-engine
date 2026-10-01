# Releasing Capsule

A release is an annotated `v<major>.<minor>.<patch>` tag on `main`. Pushing the tag runs
`.github/workflows/packages.yml`, which builds, tests, packs every `JAG.Capsule.*` package at that
version and pushes them to NuGet.org. Nothing else publishes.

## Publishing ownership

The nuget.org organization `JAG-Studios` owns every `JAG.Capsule*` package. It also owns the
trusted-publishing policy for this repository's `packages.yml`. The Actions variable `NUGET_USER`
names a member's nuget.org username, which is a user profile name and never the organization. A
member who takes over releases adds their own organization-owned policy.

## 1. Start from a green, pushed `main`

```bash
git switch main
git status --short            # must print nothing
git fetch origin main --tags
git status -sb                # must say "up to date" with origin/main, not ahead or behind
gh run list --branch main --workflow ci.yml --limit 1   # the newest CI run must be "success"
```

If CI on `HEAD` is red or still running, stop: a tag publishes whatever it points at.

## 2. Run the gates locally

The pre-commit hook runs the first four. The last one runs the NativeAOT smoke, which the build
compiles but does not run. The smoke plays twice and expects the second run to read the first's
save. It needs a saves directory, because a headless run persists nothing unless one is named.

```bash
dotnet restore --locked-mode
dotnet build --no-restore
dotnet format --verify-no-changes --no-restore
dotnet test --no-build
dotnet run --no-build --project tests/Capsule.AotSmoke/Capsule.AotSmoke.csproj -- --saves "$(mktemp -d)"
```

Every command must exit 0. Do not run `dotnet test --no-build` after a failed build: it runs the
last binaries that did build and reports green.

## 3. Choose the version

```bash
git tag --sort=-v:refname | head -1      # the current release
```

Before 1.0, bump patch for compatible fixes and minor for additions or breaking public-contract
changes. At and after 1.0, bump per SemVer. A version pushed to NuGet.org can never be reused or
overwritten, only unlisted. A broken release is followed by a new patch, never re-tagged.

## 4. Tag and push

```bash
VERSION=0.x.y
git tag -a "v$VERSION" -m "Capsule $VERSION"
git push origin "v$VERSION"
```

If the push fails, delete the local tag (`git tag -d "v$VERSION"`) before retrying.

## 5. Validate the publish

```bash
gh run list --workflow packages.yml --limit 1                          # find the run
gh run watch <run-id> --exit-status                                    # wait for it
gh run view <run-id> --log | grep "Your package was pushed"            # one line per package
```

NuGet.org indexes a pushed package minutes after the workflow reports success. Confirm each
package is downloadable before pointing a consumer at it (HTTP 200; 404 means still indexing):

```bash
for p in jag.capsule jag.capsule.build jag.capsule.runtime jag.capsule.runtime.desktop; do
  curl -s -o /dev/null -w "$p %{http_code}\n" "https://api.nuget.org/v3-flatcontainer/$p/$VERSION/$p.$VERSION.nupkg"
done
```

## 6. Move the consumers

Each consumer pins an exact version on each Capsule `PackageReference`, as
[`docs/build-and-publish.md`](docs/build-and-publish.md#consuming-capsule) shows. The engine
releases first, then the module, then the games:

1. `capsule-engine-tiled`: follow its `RELEASING.md`. It pins the new `JAG.Capsule` and
   `JAG.Capsule.Build`, regenerates its lock files and releases the module.
2. Each game consuming the packages: bump the version on its `JAG.Capsule*` references, including
   `JAG.Capsule.Tiled` once the module has released. Then regenerate its committed lock files in
   package mode with `dotnet restore -p:CapsuleSourcePath= --force-evaluate`.

## Undoing a mistake

- **Tag pushed, workflow failed:** fix `main`, then release the next patch. Delete the failed tag
  locally and remotely only if nothing was pushed to NuGet.org (`gh run view <run-id> --log` shows
  no "Your package was pushed" line): `git push origin ":refs/tags/v$VERSION" && git tag -d "v$VERSION"`.
- **Packages published but broken:** unlist them on NuGet.org and release the next patch. Never
  delete a tag that has published.
