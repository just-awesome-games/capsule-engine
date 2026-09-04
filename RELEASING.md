# Releasing Capsule

A release is an annotated `v<major>.<minor>.<patch>` tag on `main`. Pushing the tag runs
`.github/workflows/packages.yml`, which builds, tests, packs every `JAG.Capsule.*` package at that
version and pushes them to NuGet.org. Nothing else publishes.

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

The pre-commit hook runs the first four; the build compiles the NativeAOT smoke with the rest of
the solution. The last one boots that smoke, which the build alone does not do.

```bash
dotnet restore --locked-mode
dotnet build --no-restore
dotnet format --verify-no-changes --no-restore
dotnet test --no-build
dotnet run --no-build --project tests/Capsule.AotSmoke/Capsule.AotSmoke.csproj
```

Every command must exit 0. Do not run `dotnet test --no-build` after a failed build: it runs the
last binaries that did build and reports green.

## 3. Choose the version

```bash
git tag --sort=-v:refname | head -1      # the current release
```

Bump per SemVer: patch for fixes, minor for additive engine surface, major for a break in
`docs/consuming-capsule.md`'s contract. A version pushed to NuGet.org can never be reused or
overwritten, only unlisted, so a broken release is followed by a new patch, never re-tagged.

## 4. Tag and push

```bash
git tag -a v0.6.0 -m "Capsule 0.6.0"
git push origin v0.6.0
```

If the push fails, delete the local tag (`git tag -d v0.6.0`) before retrying.

## 5. Validate the publish

```bash
gh run list --workflow packages.yml --limit 1                          # find the run
gh run watch <run-id> --exit-status                                    # wait for it
gh run view <run-id> --log | grep "Your package was pushed"            # one line per package
```

NuGet.org indexes a pushed package minutes after the workflow reports success. Confirm each
package is downloadable before pointing a consumer at it (HTTP 200; 404 means still indexing):

```bash
for p in jag.capsule jag.capsule.build jag.capsule.runtime; do
  curl -s -o /dev/null -w "$p %{http_code}\n" "https://api.nuget.org/v3-flatcontainer/$p/0.6.0/$p.0.6.0.nupkg"
done
```

## 6. Move the consumers

Each consumer pins an exact `CapsuleVersion`. After a release:

- `capsule-engine-tiled`: follow its `RELEASING.md` (bump the pin, regenerate its lock files,
  release the module).
- Each game consuming the packages: bump `CapsuleVersion` (and `CapsuleTiledVersion` once the
  module has released) in its `Directory.Build.props`, then `dotnet restore --force-evaluate` to
  rewrite its lock files.

## Undoing a mistake

- **Tag pushed, workflow failed:** fix `main`, then release the next patch. Delete the failed tag
  locally and remotely only if nothing was pushed to NuGet.org (`gh run view <run-id> --log` shows
  no "Your package was pushed" line): `git push origin :refs/tags/v0.6.0 && git tag -d v0.6.0`.
- **Packages published but broken:** unlist them on NuGet.org and release the next patch. Never
  delete a tag that has published.
