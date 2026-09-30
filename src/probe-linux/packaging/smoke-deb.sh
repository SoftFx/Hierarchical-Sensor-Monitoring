#!/usr/bin/env bash
# Install smoke for a built hsm-linux-probe .deb, run as root inside a CLEAN debian:13 container
# (nothing of the build environment in it), so apt has to resolve the package's Depends from the
# Debian archive exactly as on a real host:
#
#   docker run --rm -v <repo>:/src -w /src/src/probe-linux debian:13 \
#       bash packaging/smoke-deb.sh dist/hsm-linux-probe_<version>_amd64.deb [<probe-version>]
#
# (from Git Bash on Windows prefix it with MSYS_NO_PATHCONV=1.) Used by the probe CI lane and the
# probe-v* release lane (.github/workflows/probe-linux.yml, probe-release.yml).
#
# It checks what a container can prove without systemd as PID 1: the package metadata and layout,
# that apt installs it on debian:13, that the binary runs and reports <probe-version> (default: the
# package version up to a '~' suffix), that postinst created the service user and seeded the config,
# that the probe keeps running against an unreachable server and stops cleanly on SIGTERM, that a
# reinstall leaves an edited config alone with no dpkg prompt, and that purge removes what the
# package owns. The systemd unit itself (hardening, LoadCredential, StateDirectory) needs a real
# host: see the runbook (src/probe-linux/RUNBOOK.md).
set -euo pipefail

DEB="${1:-}"
if [[ -z "$DEB" || ! -f "$DEB" ]]; then
    echo "usage: $0 <path to hsm-linux-probe_*.deb> [<expected probe version>]" >&2
    exit 2
fi
DEB="$(cd "$(dirname "$DEB")" && pwd)/$(basename "$DEB")"

fail() {
    echo "SMOKE FAILED: $*" >&2
    exit 1
}

export DEBIAN_FRONTEND=noninteractive

echo "==> package metadata"
dpkg-deb --info "$DEB"
[[ "$(dpkg-deb -f "$DEB" Package)" == "hsm-linux-probe" ]] || fail "Package is not hsm-linux-probe"
PKG_VERSION="$(dpkg-deb -f "$DEB" Version)"
EXPECTED="${2:-${PKG_VERSION%%~*}}"
[[ "$(basename "$DEB")" == "hsm-linux-probe_${PKG_VERSION}_$(dpkg-deb -f "$DEB" Architecture).deb" ]] ||
    fail "file name $(basename "$DEB") does not match the control fields"

echo "==> package contents"
CONTENTS="$(dpkg-deb --contents "$DEB")"
echo "$CONTENTS"
for path in ./usr/bin/hsm-linux-probe ./lib/systemd/system/hsm-linux-probe.service \
    ./usr/share/hsm-linux-probe/config.example.json ./usr/lib/hsm-linux-probe/docker-access.sh \
    ./usr/share/doc/hsm-linux-probe/copyright ./etc/hsm-linux-probe/; do
    grep -qF " $path" <<<"$CONTENTS" || fail "the package does not ship $path"
done
# The operator's config is not a conffile (0.3.1, #1484): nothing may ship under /etc but the
# directory itself, and the control archive must carry no conffiles list.
if grep -E ' \./etc/hsm-linux-probe/.+' <<<"$CONTENTS"; then
    fail "the package ships a file under /etc/hsm-linux-probe (the config must not be a conffile)"
fi
CONTROL="$(mktemp -d)"
dpkg-deb --control "$DEB" "$CONTROL"
[[ ! -e "$CONTROL/conffiles" ]] || fail "the package declares conffiles: $(cat "$CONTROL/conffiles")"

echo "==> apt install (Depends resolved from the debian:13 archive)"
apt-get update -qq
apt-get install -y -qq "$DEB" >/dev/null

echo "==> installed binary"
ldd /usr/bin/hsm-linux-probe
if ldd /usr/bin/hsm-linux-probe | grep -q 'not found'; then
    fail "the installed binary has an unresolved shared library"
fi
VERSION_LINE="$(hsm-linux-probe --version)"
echo "$VERSION_LINE"
[[ "$VERSION_LINE" == "hsm-linux-probe $EXPECTED "* ]] ||
    fail "--version says '$VERSION_LINE', expected probe version $EXPECTED"

echo "==> postinst results"
PASSWD="$(getent passwd hsm-probe)" || fail "postinst did not create the hsm-probe user"
echo "$PASSWD"
[[ "$PASSWD" == *":/nonexistent:/usr/sbin/nologin" ]] || fail "hsm-probe has an unexpected home or shell"
getent group hsm-probe >/dev/null || fail "postinst did not create the hsm-probe group"
cmp /usr/share/hsm-linux-probe/config.example.json /etc/hsm-linux-probe/config.json ||
    fail "postinst did not seed /etc/hsm-linux-probe/config.json from the skeleton"
# No docker group in this container, so docker-access.sh must not have written the drop-in.
[[ ! -e /etc/systemd/system/hsm-linux-probe.service.d/docker.conf ]] ||
    fail "the Docker drop-in was written on a host without a docker group"

echo "==> run against an unreachable server, then SIGTERM"
WORK="$(mktemp -d)"
(umask 077 && printf 'smoke-not-a-real-key\n' >"$WORK/access-key")
cat >"$WORK/config.json" <<EOF
{
  "hsm": { "address": "https://127.0.0.1", "port": 9, "accessKeyFile": "$WORK/access-key" },
  "logging": { "directory": "$WORK/log", "level": "info" }
}
EOF
# STATE_DIRECTORY stands in for the unit's StateDirectory=, which only systemd creates.
mkdir -m 0700 "$WORK/state"
STATE_DIRECTORY="$WORK/state" hsm-linux-probe --config "$WORK/config.json" >"$WORK/stderr.log" 2>&1 &
PID=$!
sleep 6
if ! kill -0 "$PID" 2>/dev/null; then
    cat "$WORK/stderr.log"
    fail "the probe exited within 6 s against an unreachable server (it must keep running)"
fi
kill -TERM "$PID"
STATUS=0
wait "$PID" || STATUS=$?
cat "$WORK/stderr.log"
[[ "$STATUS" -eq 0 ]] || fail "the probe exited with $STATUS after SIGTERM, expected 0"
ls "$WORK"/log/hsm-linux-probe_*.log >/dev/null 2>&1 || fail "no log file in the configured directory"
if grep -q 'smoke-not-a-real-key' "$WORK/stderr.log" "$WORK"/log/*.log; then
    fail "the access key appears in the log"
fi

echo "==> reinstall keeps an edited config (no conffile prompt)"
sed -i 's/"level": "info"/"level": "warn"/' /etc/hsm-linux-probe/config.json
EDITED="$(sha256sum /etc/hsm-linux-probe/config.json)"
apt-get install -y -qq --reinstall "$DEB" </dev/null >/dev/null
[[ "$(sha256sum /etc/hsm-linux-probe/config.json)" == "$EDITED" ]] || fail "the reinstall changed the operator's config"
if ls /etc/hsm-linux-probe/*.dpkg-* >/dev/null 2>&1; then
    fail "dpkg left conffile debris: $(ls /etc/hsm-linux-probe/)"
fi

echo "==> purge"
apt-get purge -y -qq hsm-linux-probe >/dev/null
[[ ! -e /usr/bin/hsm-linux-probe ]] || fail "purge left /usr/bin/hsm-linux-probe"
[[ ! -e /etc/hsm-linux-probe ]] || fail "purge left /etc/hsm-linux-probe: $(ls -A /etc/hsm-linux-probe)"

echo "SMOKE OK: hsm-linux-probe $PKG_VERSION installs, runs, stops, reinstalls and purges on $(grep '^PRETTY_NAME=' /etc/os-release | cut -d= -f2- | tr -d '"')"
