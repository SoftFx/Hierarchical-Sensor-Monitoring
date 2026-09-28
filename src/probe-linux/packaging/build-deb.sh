#!/usr/bin/env bash
# Build the hsm-linux-probe .deb reproducibly inside a plain debian:13 container.
#
#   docker run --rm -v <repo>:/src -w /src/src/probe-linux \
#       -v hsm-probe-cargo:/root/.cargo debian:13 bash packaging/build-deb.sh 0.2.0~trial1
#
# (from Git Bash on Windows prefix it with MSYS_NO_PATHCONV=1 so /src is not rewritten). The
# optional named volume caches rustup, the toolchain and the crate registry between runs.
#
# Output: dist/hsm-linux-probe_<version>_<arch>.deb next to this directory's parent
# (src/probe-linux/dist/), with the layout, maintainer scripts and Depends of the hand-built
# 0.1.0~trial* packages:
#   /usr/bin/hsm-linux-probe
#   /lib/systemd/system/hsm-linux-probe.service     (kept under /lib, where the trials put it:
#                                                    moving a file between /lib and /usr/lib across
#                                                    versions is unsafe with dpkg on a merged /usr)
#   /etc/hsm-linux-probe/config.json                (conffile: operator edits survive upgrades)
#   /usr/share/doc/hsm-linux-probe/copyright
# postinst creates the hsm-probe system user/group and reloads systemd; a fresh install does NOT
# enable or start the unit, an upgrade restarts it if it was running (prerm leaves a /run marker);
# prerm disables it on remove; postrm purges state/logs.
#
# The binary is built with the collector's Linux metric sources (--features
# linux-default-sensors), i.e. the full managed-parity set plus the probe-only sensors.
set -euo pipefail

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
    echo "usage: $0 <debian-version, e.g. 0.2.0~trial1>" >&2
    exit 2
fi
# Debian version syntax without an epoch: starts with a digit, then [A-Za-z0-9.+~-].
if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9.+~-]*$ ]]; then
    echo "invalid Debian version '$VERSION'" >&2
    exit 2
fi

PROBE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGING_DIR="$PROBE_DIR/packaging"
DIST_DIR="$PROBE_DIR/dist"
# The target dir lives outside the (possibly bind-mounted, slow) source tree.
export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-/tmp/hsm-linux-probe-target}"
export CARGO_HOME="${CARGO_HOME:-$HOME/.cargo}"
# rustup's home goes inside CARGO_HOME so one cached volume holds everything.
export RUSTUP_HOME="${RUSTUP_HOME:-$CARGO_HOME/rustup}"
export PATH="$CARGO_HOME/bin:$PATH"

echo "==> build dependencies"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends \
    build-essential cmake pkg-config libcurl4-openssl-dev ca-certificates curl \
    dpkg-dev file >/dev/null

if ! command -v cargo >/dev/null 2>&1; then
    echo "==> rustup (stable, minimal profile)"
    curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
        | sh -s -- -y --no-modify-path --profile minimal --default-toolchain stable
fi
rustc --version
cargo --version

echo "==> cargo build (release, linux-default-sensors)"
cd "$PROBE_DIR"
cargo build --release --locked -p hsm-linux-probe --features linux-default-sensors
BINARY="$CARGO_TARGET_DIR/release/hsm-linux-probe"
"$BINARY" --version

# The Depends line below is fixed, so prove the binary needs nothing outside it: every shared
# library it loads must come from libcurl4t64, libc6, libstdc++6 or libgcc-s1.
echo "==> shared-library closure"
NEEDED="$(objdump -p "$BINARY" | awk '/NEEDED/ {print $2}' | sort)"
echo "$NEEDED"
while read -r lib; do
    case "$lib" in
        libcurl.so.4 | libstdc++.so.6 | libgcc_s.so.1 | libc.so.6 | libm.so.6 | \
            ld-linux-x86-64.so.2 | libdl.so.2 | libpthread.so.0 | librt.so.1) ;;
        *)
            echo "unexpected shared library dependency '$lib': update Depends in build-deb.sh" >&2
            exit 1
            ;;
    esac
done <<<"$NEEDED"

ARCH="$(dpkg --print-architecture)"
PACKAGE="hsm-linux-probe_${VERSION}_${ARCH}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
ROOT="$STAGE/$PACKAGE"

echo "==> staging $PACKAGE"
install -d -m 0755 "$ROOT/DEBIAN" "$ROOT/usr/bin" "$ROOT/lib/systemd/system" \
    "$ROOT/etc/hsm-linux-probe" "$ROOT/usr/share/doc/hsm-linux-probe"
install -m 0755 "$BINARY" "$ROOT/usr/bin/hsm-linux-probe"
strip --strip-unneeded "$ROOT/usr/bin/hsm-linux-probe"
install -m 0644 "$PACKAGING_DIR/hsm-linux-probe.service" "$ROOT/lib/systemd/system/"
install -m 0644 "$PACKAGING_DIR/config.example.json" "$ROOT/etc/hsm-linux-probe/config.json"
install -m 0644 "$PACKAGING_DIR/deb/copyright" "$ROOT/usr/share/doc/hsm-linux-probe/copyright"
for script in postinst prerm postrm; do
    install -m 0755 "$PACKAGING_DIR/deb/$script" "$ROOT/DEBIAN/$script"
done
echo "/etc/hsm-linux-probe/config.json" >"$ROOT/DEBIAN/conffiles"

INSTALLED_SIZE="$(du -sk --exclude=DEBIAN "$ROOT" | cut -f1)"
cat >"$ROOT/DEBIAN/control" <<EOF
Package: hsm-linux-probe
Version: $VERSION
Section: admin
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_SIZE
Depends: libcurl4t64, ca-certificates, libc6, libstdc++6, libgcc-s1
Maintainer: HSM team <hsm@soft-fx.invalid>
Homepage: https://github.com/SoftFx/Hierarchical-Sensor-Monitoring
Description: HSM Linux host probe
 Systemd-hosted probe that reports Linux host metrics into an HSM server
 through the shared native collector: the managed-collector parity set plus
 the probe-only host and disk sensors. Built by
 src/probe-linux/packaging/build-deb.sh.
EOF

# Reproducible timestamps: every file gets the time of the newest source commit when known.
if [[ -n "${SOURCE_DATE_EPOCH:-}" ]]; then
    find "$ROOT" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
fi

mkdir -p "$DIST_DIR"
dpkg-deb --root-owner-group -Zxz --build "$ROOT" "$DIST_DIR/$PACKAGE.deb" >/dev/null

echo "==> $DIST_DIR/$PACKAGE.deb"
dpkg-deb --info "$DIST_DIR/$PACKAGE.deb"
dpkg-deb --contents "$DIST_DIR/$PACKAGE.deb"
sha256sum "$DIST_DIR/$PACKAGE.deb"
