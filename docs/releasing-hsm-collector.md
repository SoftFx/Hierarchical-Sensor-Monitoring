# Releasing the native C++ collector (`hsm-collector`)

How to publish a new version of the native collector (`src/native/collector`) so downstream projects
can consume it. This is the **maintainer** procedure; consumers only need the wiki page
[C++ Data Collector](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Cpp-Data-Collector).

> **A release here is a git tag + a registry entry — not a GitHub Release with binaries.** This
> repository *is* a [vcpkg registry](https://learn.microsoft.com/vcpkg/produce/publish-to-a-git-registry):
> consumers run `vcpkg install hsm-collector`, and vcpkg builds it from the tagged source. There is
> nothing to upload.

---

## The version lives in four places

They must always move together. CI enforces it (`hsm-collector version check`), and you can check
locally at any time:

```bash
python scripts/check-collector-version.py
```

| File | Declaration |
|---|---|
| `src/native/collector/CMakeLists.txt` | `project(HsmCollectorNative VERSION X.Y.Z …)` |
| `src/native/collector/include/hsm_collector/hsm_collector.h` | `HSM_COLLECTOR_VERSION_MAJOR/MINOR/PATCH` (the C ABI semver) |
| `src/native/collector/vcpkg-port/vcpkg.json` | overlay port used by CI |
| `src/native/collector/conanfile.py` | conan recipe |

`ports/hsm-collector/` and `versions/` (the registry itself) are **not** in that list — the publish
automation rewrites them for you.

---

## Procedure

### 1. Change the code and bump the version

Make your change in `src/native/collector/`, then bump all four declarations above in the *same* PR.
Semver against the **C ABI**: patch = fixes, minor = additive API, major = breaking ABI.

### 2. Merge to `master`

Normal PR flow. The version-check lane fails the PR if the four declarations disagree.

### 3. Tag the merged commit

```bash
git fetch origin master
git tag collector-v0.6.3 origin/master     # tag name must match the version you just bumped to
git push origin collector-v0.6.3
```

### 4. The automation opens a registry PR

Pushing the tag triggers
[`hsm-collector-registry-publish`](../.github/workflows/hsm-collector-registry-publish.yml), which:

1. downloads the tag's source tarball and computes its SHA512,
2. verifies the tagged source really declares that version,
3. rewrites `ports/hsm-collector/` (`REF`, `SHA512`, `version`),
4. updates `versions/` (baseline + `version` → `git-tree`), advancing the baseline only,
5. builds and installs the updated port (`hsm-collector[http]`) to prove it resolves, and
6. opens a PR.

### 5. Run the Windows check, then merge that PR

The auto-PR is opened with `GITHUB_TOKEN`, so GitHub will **not** auto-trigger the consumer check on
it. Run it by hand before merging:

**Actions → `hsm-collector vcpkg registry` → Run workflow → pick the `registry/hsm-collector-<ver>`
branch.** (The publish run itself only validated on Linux; this lane is the Windows/MSVC path.)

Merge once it is green. **The version is live for consumers at that moment.**

### 6. Tell consumers (optional)

They pick it up with:

```bash
vcpkg x-update-baseline    # moves their registry baseline to your newest commit
vcpkg install hsm-collector[http]
```

---

## Doing it manually (if the automation is unavailable)

The automation only saves typing; the same result by hand:

1. `curl -fsSL https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/archive/collector-v0.6.3.tar.gz | sha512sum`
2. In `ports/hsm-collector/portfile.cmake` set `REF collector-v0.6.3` and that `SHA512`; set `version`
   in `ports/hsm-collector/vcpkg.json`.
3. Commit the port, then record its tree: `git rev-parse HEAD:ports/hsm-collector`.
4. Add `{ "version": "0.6.3", "git-tree": "<that tree>" }` to the **front** of
   `versions/h-/hsm-collector.json`, and set `baseline` in `versions/baseline.json`.
5. PR it, and run the `hsm-collector vcpkg registry` check.

(Equivalent to `vcpkg x-add-version hsm-collector`, if you have vcpkg to hand.)

---

## Notes and limits

- **Port-only fixes** (changing the portfile without a source change) need a `port-version` bump and
  are done by hand — the automation publishes source versions only.
- **Re-running for an already-published version** is a safe no-op, *unless* the source content changed:
  vcpkg treats a `version` → `git-tree` mapping as immutable, so the run refuses rather than rewriting
  history. Bump the version instead.
- **Two publishes open at once** will conflict in `versions/` — merge the first, rebase the second.
- **Never move or delete a `collector-v*` tag.** Consumers' pinned SHA512 is computed from that exact
  tarball; recreating the tag breaks every pinned install.
