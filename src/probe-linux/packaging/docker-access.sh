#!/bin/sh
# Grant or revoke hsm-linux-probe's access to the Docker Engine API socket (#1416).
#
# The socket (/var/run/docker.sock, srw-rw---- root:docker) is root-equivalent: anyone who can
# write to it can start a privileged container. The probe only ever issues four read-only GETs
# (initiative docs/initiatives/linux-docker-probe.md §4.3), but that restriction is by
# construction and review, not by the kernel — so access is granted only where a docker group
# exists, and only to this one unit.
#
# Why a drop-in and not SupplementaryGroups= in the unit itself: systemd refuses to start a unit
# whose supplementary group does not exist, so hardcoding it would break the probe on every host
# without Docker. This script writes the drop-in only when `getent group docker` succeeds.
#
# Installed as /usr/lib/hsm-linux-probe/docker-access.sh and called by the package's postinst
# (`install`); postrm deletes the same drop-in itself on remove/purge (this script is gone by then),
# and `remove` is here for an operator taking the socket away by hand. After installing Docker on a
# host that already has the probe, run it by hand:
#   sudo /usr/lib/hsm-linux-probe/docker-access.sh install && sudo systemctl restart hsm-linux-probe
#
# The caller reloads systemd; this script only writes or removes the file.
set -eu

DROPIN_DIR="${HSM_PROBE_DROPIN_DIR:-/etc/systemd/system/hsm-linux-probe.service.d}"
DROPIN="$DROPIN_DIR/docker.conf"

case "${1:-}" in
install)
    if getent group docker >/dev/null 2>&1; then
        mkdir -p "$DROPIN_DIR"
        tmp="$DROPIN.tmp.$$"
        cat >"$tmp" <<'EOF'
# Written by hsm-linux-probe's package (docker-access.sh) because this host has a docker group.
# Grants read access to the Docker Engine API socket for the Docker source (#1416).
# Remove this file (and daemon-reload) to take the socket away; the probe then logs one error and
# reports no Docker sensors, everything else keeps working.
[Service]
SupplementaryGroups=docker
EOF
        chmod 0644 "$tmp"
        mv -f "$tmp" "$DROPIN"
        echo "hsm-linux-probe: docker group found; Docker socket access granted via $DROPIN"
    else
        # A stale drop-in from a host that has since lost its docker group would stop the unit.
        rm -f "$DROPIN"
        rmdir "$DROPIN_DIR" 2>/dev/null || true
        echo "hsm-linux-probe: no docker group on this host; the Docker source stays without socket access"
    fi
    ;;
remove)
    rm -f "$DROPIN"
    rmdir "$DROPIN_DIR" 2>/dev/null || true
    ;;
*)
    echo "usage: $0 install|remove" >&2
    exit 2
    ;;
esac
