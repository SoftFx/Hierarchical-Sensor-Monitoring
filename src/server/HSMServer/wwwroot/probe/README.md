# Linux probe package drop-point

The per-product Linux probe download (`GET /api/agent/linux-installer?productId=…`, #1424) serves a
`.tar.gz` built around the **`hsm-linux-probe_<version>_<arch>.deb`** staged in this directory, plus a
generated `config.json`, the product key in `access-key`, the server's public CA chain
(`server-ca.pem`, only when this server terminates TLS itself) and `install.sh`/`uninstall.sh`.

The `.deb` is served **byte-identical** to the `probe-v<version>` GitHub Release asset. Only the files
around it are generated per product.

## What is generated vs. checked in

The `.deb` is a **staged release artifact, not a source** and is gitignored. Both `server-build.yml`
legs and `scripts/local-docker-build.ps1` read `src/server/HSMServer/probe-release.txt`:

- **Empty pin (the state until the first `probe-v*` release exists):** staging is skipped, the build
  does not fail, and the endpoint answers HTTP 503 with a clear message.
- **Pinned version:** `gh release download probe-v<pin>`, SHA-256 check against the release's
  `.deb.sha256`, then the `.deb` is staged here. The publish output and the Docker image are then
  required to contain exactly one non-empty `hsm-linux-probe_*.deb`.

Exactly one package is expected here; staging clears older ones first. With none or several, the
endpoint answers 503.
