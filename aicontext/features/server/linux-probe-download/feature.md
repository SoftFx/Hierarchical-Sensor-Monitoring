# Feature: Per-product Linux probe download (server side)

> Owner: server | Last reviewed: 2026-09-22 | Canonical: yes
> Scope: the admin-only endpoint + UI that give an operator a ready-to-run, per-product HSM Linux probe
> bundle (#1424, epic #1413, initiative `docs/initiatives/linux-docker-probe.md` §4.6). The Linux sibling of
> `../agent-download/feature.md` (Windows agent); the probe itself lives in `src/probe-linux/`.

---

## Description

An admin opens a product's edit page ("HSM Agent" section) and clicks **Download Linux probe** next to
**Download agent**. The server streams `hsm-linux-probe-<product>.tar.gz`. On the host:

```
tar xzf hsm-linux-probe-<product>.tar.gz && sudo ./hsm-linux-probe-<product>/install.sh
```

installs the package, places config + key (+ CA), enables the systemd unit and prints its status: the probe
is connected with no manual configuration. It is deliberately **not** a `curl … | sudo bash` one-liner: the
endpoint needs an admin session, and a token in a URL would leak a bearer credential into shell history,
proxy logs and process args (§4.3).

## Bundle contents

Every entry sits in one top-level folder `hsm-linux-probe-<product>/`, so extracting never scatters files
(a key included) into the current directory or mixes two products' bundles. `<product>` is
`LinuxProbeInstallerBundle.ShellSafeName`: product names allow `; & * ( ) #`, and the folder is typed into a
root shell, so anything outside `[A-Za-z0-9._-]` becomes `_` (runs collapsed, leading `.`/`-`/`_` dropped,
empty → `product`). The download file name uses the same form. Two products whose names reduce to the
same form (`Prod (EU)` / `Prod EU`) get the same folder name; extract each bundle in its own directory.

| Entry | Mode | Content |
|---|---|---|
| `hsm-linux-probe_<ver>_<arch>.deb` | 0644 | **Byte-identical** to the staged `probe-v*` release asset, under its release file name |
| `config.json` | 0644 | Probe schema: `hsm.address` + `hsm.port` from `AgentConnectionResolver`, `hsm.accessKeyFile` = `/run/credentials/hsm-linux-probe.service/access-key` (the unit's `LoadCredential=` path), `hsm.module` = `LinuxProbe`, `hsm.computerName` = `"auto"`. **No key.** |
| `access-key` | 0600 | The product key from `AgentKeySelector` (same selection as Windows), newline-terminated |
| `server-ca.pem` | 0644 | Optional, the server's leaf certificate (public part only); see *TLS* |
| `install.sh`, `uninstall.sh` | 0755 | LF line endings; shellcheck-clean |

All entries are ustar entries owned by uid/gid 0; the archive is built in memory with
`System.Formats.Tar` + `GZipStream` at `CompressionLevel.Fastest` (the `.deb`, the bulk of it, is already
compressed).

## Invariants

- **Admin-only.** `[AuthorizeIsAdmin]` on `GET /api/agent/linux-installer`; the button renders only for admins.
- **The key is never in `config.json`**, never an argument, never echoed. install.sh moves it with
  `install -m 0400 -o root -g root` to `/etc/hsm-linux-probe/access-key` (the unit's `LoadCredential=` source)
  and then `shred -u`s the extracted copy.
- **The .deb is served byte-identical** to the release (package signatures/checksums stay valid).
- **No allow-untrusted switch.** The probe always verifies peer + hostname; trust for a self-signed server
  comes from `server-ca.pem` in the system store.
- **HTTPS only.** The probe's config parser rejects `http://`, so an `http://` resolved address is answered
  with 400 and a pointer to Configuration → Agent → *Agent connection URL*, instead of a bundle that never
  connects.
- **Not staged ⇒ 503** with a clear message (no web root, no `wwwroot/probe/`, no `.deb`, or more than one).
  This is checked first, so an out-of-the-box server (no release pinned, default certificate) reports the
  missing package, not the certificate.
- **Never cacheable.** The response carries a bearer credential: `Installer` and `LinuxInstaller` are
  `[ResponseCache(NoStore = true, Location = None)]`, the `ProductController` convention. The self-update
  endpoints stay cacheable.

## TLS: when `server-ca.pem` ships

`LinuxProbeServerCa.Decide(serverTerminatesTls, isBundledDefault, hasCertificate)` — pure, tested:

- **Include** when Kestrel itself terminated TLS on the download request (an `ITlsHandshakeFeature` is
  present on the connection) with an admin-configured certificate. Content: the public part
  (`ExportCertificatePem`) of the **same `X509Certificate2` instance Kestrel installed at startup**
  (`ServerCertificateConfig.Certificate`), with `IsBundledDefault` recorded when that instance was loaded.
  A certificate saved in settings but not yet applied by a restart is therefore ignored, and the `.pfx` is
  not re-read per download.
  Only the leaf: an issuing CA that a `.pfx` may also carry would become a system-wide trust anchor for every
  host the probe machine talks to, while the leaf vouches only for this server. A CA-issued leaf alone
  verifies because libcurl sets OpenSSL's partial-chain flag by default, and the collector keeps libcurl's
  TLS defaults. This was checked on Debian 13 (curl 8.14.1 / OpenSSL 3.5): a leaf issued by a private CA
  answered 200 both as `--cacert` and from the system store after `update-ca-certificates`.
- **Refuse (400)** when Kestrel serves the bundled `default.server.pfx`. That file ships in the repository
  with its private key, so installing it as a trust anchor would let anyone impersonate any name in its SAN
  to every TLS client on the host. The admin must configure a server certificate (or run behind the proxy)
  first. Windows solves the same case with the process-scoped `allowUntrustedCertificate`, which the probe
  deliberately does not have.
- **Omit** behind a TLS-terminating proxy (plain-HTTP mode + Caddy/Let's Encrypt, #1411): no TLS on the
  Kestrel connection, and the proxy's certificate is already publicly trusted.
- **Omit, reported** when the certificate cannot be loaded: the download still succeeds, and the server logs
  a warning that the probe will only connect if that certificate is already trusted on its host. In practice
  Kestrel would not have started with an unloadable certificate.

**Known limit: the signal is the admin's connection, not the probe's.** The download arrives on the site
port; the probe connects to the sensor port at the resolved address. Today both are Kestrel listeners with
the same certificate, so they agree. They can disagree in split deployments, and the result is then wrong:

| Deployment | Result | Should be |
|---|---|---|
| Proxy terminates TLS for the UI only; the sensor port is exposed directly with a self-signed cert | Omit (probe cannot verify) | Include |
| Proxy re-encrypts to Kestrel, which still serves the bundled default | 400 | Omit |
| Proxy re-encrypts to Kestrel with an internal-name cert | Include (unused anchor on the host) | Omit |

The fix is an explicit admin choice (or a probe of the resolved address). It is deferred until #1411's
plain-HTTP mode lands and settles which topologies HSM supports. Until then, an operator in the first case can
copy the server's certificate to `/usr/local/share/ca-certificates/` by hand.

Deciding from the connection rather than from a config flag keeps the rule correct both before and after
the plain-HTTP mode of #1411 is enabled, with no coupling to that setting.

## install.sh / uninstall.sh

install.sh (`set -euo pipefail`, refuses non-root, one `hsm-linux-probe_*.deb` expected next to it):

1. Places `config.json` in `/etc/hsm-linux-probe/` **only if none exists** (or with `--force-config`),
   replacing `"computerName": "auto"` with the host's short name (see *Gap*); if the substitution matched
   nothing it stops instead of installing `"auto"` (every host would collapse onto one node). Places the key if the bundle
   still has it; a re-run after the key was consumed keeps the installed one.
   An `EXIT` trap, armed just before the key is copied, shreds the extracted copy on every exit path, a
   failing copy included.
2. `apt-get update` (a failure only warns: an unreachable mirror is not fatal by itself), then `apt-get install -y ./hsm-linux-probe_*.deb ca-certificates` with
   `--force-confold`, so the config written in step 1 wins over the package's placeholder conffile.
   Config and key go in **before** the package so the unit's first start already finds them.
   `ca-certificates` is installed always: the probe verifies the server against the system trust store in
   every case, including behind a public-CA proxy where no `server-ca.pem` ships, and minimal hosts lack it.
3. With `server-ca.pem`: copies it to `/usr/local/share/ca-certificates/hsm-server.crt`, runs
   `update-ca-certificates`, and prints the certificate's expiry date.
4. `systemctl enable --now hsm-linux-probe`, then `restart` (a reinstall picks up the new key/config/CA).
5. Waits 3 s, prints `systemctl status --no-pager`, exits non-zero if the unit is not active. On success it
   reminds the operator that the downloaded `.tar.gz` still contains the key and should be deleted: the script
   cannot know where the archive is, and the archive keeps the browser's/scp's default permissions.

**Certificate renewal.** Because only the leaf is trusted, renewing the server certificate (a new leaf) stops
every installed probe from verifying the server. The procedure is: after the renewal, download the bundle again
and re-run `install.sh` on each host. That replaces `hsm-server.crt` and keeps the config and key. A
probe-scoped CA file (`ca_file` → `CURLOPT_CAINFO`, initiative §4.1) would allow trusting a private issuing CA
without making it system-wide; it is not on this PR's path.

uninstall.sh: disables and stops the unit, `apt-get purge`s the package, removes the key, config (incl.
`.dpkg-dist`/`.dpkg-old`) and CA file, refreshes the trust store (plain `update-ca-certificates`, not
`--fresh`, so hand-made links in `/etc/ssl/certs` survive). It never contacts the HSM server —
the sensor history stays.

## Gap: `computerName: "auto"`

The probe's config parser (`src/probe-linux/hsm-linux-probe/src/config.rs`, PR #1420) does **not** resolve
`"auto"`: it passes any non-empty value verbatim to the collector, and an empty one leaves the computer node
out of the path. Until the probe resolves `"auto"` itself (like HsmAgent's `config.cpp`), install.sh does it
at install time. The server-generated config keeps `"auto"`, so nothing changes when the probe learns it.

## Staging: `probe-release.txt`

Same model as `agent-release.txt` (see `../agent-download/feature.md` → *Packaging*):

| Pin state | Staging | Guards | Endpoint |
|---|---|---|---|
| Empty or missing (no `probe-v*` release yet) | skipped, build green | inactive | 503 |
| `X.Y.Z` | `gh release download probe-vX.Y.Z` → SHA-256 check against `hsm-linux-probe_*.deb.sha256` → one `.deb` in `wwwroot/probe/` | Windows publish output and Docker image must hold exactly one non-empty `hsm-linux-probe_*.deb` | serves the bundle |

**Single architecture, deliberately.** Staging, both guards and the endpoint expect exactly one `.deb`, and
every host gets that package. When #1418 publishes more than one architecture, this becomes an `?arch=`
parameter and a per-arch staging pick; until then a second asset fails the build loudly instead of shipping
the wrong package.

The staged `.deb` is **not** served as a static file: `UseStaticFiles()` runs with the default content-type
provider, which does not map `.deb` (unlike `/agent/hsm-agent.exe`), so `/probe/<name>.deb` answers 404. The
drop-point's `README.md` is served, which is harmless. Everything per-product comes only from the admin endpoint.

Paths: `scripts/stage-linux-probe.sh` (both `server-build.yml` legs) and `scripts/local-docker-build.ps1`.
The staged `.deb` is gitignored (`wwwroot/probe/.gitignore`). Shipping a newer probe = push the
`probe-v*` tag, then bump `probe-release.txt` in a one-line PR.

## Key components

| Component | Location |
|---|---|
| Endpoint `GET /api/agent/linux-installer?productId=…` | `HSMServer/Controllers/AgentController.cs` (`LinuxInstaller`) |
| Bundle builder (config, scripts, tar.gz, staged-package pick, address check; pure) | `HSMServer/Model/Agent/LinuxProbeInstallerBundle.cs` |
| CA decision + public leaf export | `HSMServer/Model/Agent/LinuxProbeServerCa.cs` |
| Whether the certificate Kestrel loaded is the bundled default | `ServerConfiguration/Sections/ServerCertificateConfig.cs` (`IsBundledDefault`) |
| Button | `Views/Product/EditProduct.cshtml` — "HSM Agent" section (admin-only) |
| Drop-point | `HSMServer/wwwroot/probe/` (README + gitignore) |
| Tests | `tests/HSMServer.Core.Tests/LinuxProbeInstallerBundleTests.cs` (layout + top-level folder, byte-identical .deb, key only in `access-key`, config schema, tar modes/ownership, script content incl. the key-cleanup trap) + `LinuxProbeDownloadLogicTests.cs` (staged-package pick, HTTPS check, CA decision matrix, leaf-only public export, bundled-default refusal, admin guard, 503 paths, full bundle via the controller with and without Kestrel TLS) |

`install.sh`/`uninstall.sh` were also checked with shellcheck and smoke-run in a systemd `debian:13`
container against a throwaway dummy `.deb` (fresh install from an empty apt index, non-root refusal, key
shredded after a failed install, re-run idempotency, `--force-config`, uninstall twice) when #1424 landed; that run is manual, not a CI lane.

## Contract with the probe package (#1415 / #1418)

`src/probe-linux/` is not on master yet (PR #1420), so nothing in the build catches a rename on the probe
side: the bundle would install cleanly and never send a value. These must move in lockstep with the probe, and
be re-verified before the first non-empty `probe-release.txt`:

| Symbol | Here | Probe side |
|---|---|---|
| Package name `hsm-linux-probe`, asset `hsm-linux-probe_<ver>_<arch>.deb` + `.deb.sha256` | install/uninstall scripts, staging, guards | `.deb` build (#1418) |
| Unit `hsm-linux-probe.service` | `install.sh` / `uninstall.sh` | `packaging/hsm-linux-probe.service` |
| `LoadCredential=access-key:/etc/hsm-linux-probe/access-key` → `/run/credentials/hsm-linux-probe.service/access-key` | `AccessKeyCredentialPath`, `install.sh` | the unit |
| Config `/etc/hsm-linux-probe/config.json`, keys `hsm.address/port/accessKeyFile/computerName/module` | `BuildConfigJson` | `config.rs` |
| Module `LinuxProbe` | `ModuleName` | `config.rs` default |
| https-only address | `ValidateServerAddress` | `config.rs` validation |

**Not yet verified on a real host.** The `debian:13` smoke test ran against a dummy `.deb`. In that Docker
Desktop container, systemd applied no per-unit mount namespacing, so `LoadCredential=` never materialized
and the test pointed the dummy at the installed key directly. The credential hand-off to the `hsm-probe`
user must be checked on a real Debian host before the pin is set.

## Out of scope (follow-up)

- A dedicated, separately-revocable per-download key (shared follow-up with the Windows agent).
- A real `probe-v*` release (#1418); until it exists the button answers 503.
