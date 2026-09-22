#!/usr/bin/env bash
# Stage the Linux probe .deb pinned in src/server/HSMServer/probe-release.txt into
# src/server/HSMServer/wwwroot/probe/ (#1424). Used by both server-build.yml legs.
#
# Empty or missing pin = no probe release yet: skip, do not fail the build. The download endpoint then
# answers 503. With a pin: download probe-v<pin> from GitHub Releases, verify the SHA-256 published
# next to the .deb, and stage exactly one hsm-linux-probe_*.deb.
#
# Needs: gh (authenticated via GH_TOKEN), sha256sum. Repository: $GITHUB_REPOSITORY or upstream.
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
pin_file="$root/src/server/HSMServer/probe-release.txt"
dir="$root/src/server/HSMServer/wwwroot/probe"
repo="${GITHUB_REPOSITORY:-SoftFx/Hierarchical-Sensor-Monitoring}"

pin=""
if [ -f "$pin_file" ]; then
  pin="$(tr -d ' \r\n\t' < "$pin_file")"
fi

if [ -z "$pin" ]; then
  echo "probe-release.txt is empty: no Linux probe release pinned, skipping staging (the download endpoint will answer 503)."
  exit 0
fi

echo "pinned Linux probe release: probe-v$pin"
mkdir -p "$dir"
rm -f "$dir"/hsm-linux-probe_*.deb "$dir"/hsm-linux-probe_*.deb.sha256

gh release download "probe-v$pin" --repo "$repo" \
  -p 'hsm-linux-probe_*.deb' -p 'hsm-linux-probe_*.deb.sha256' -D "$dir" --clobber

shopt -s nullglob
debs=("$dir"/hsm-linux-probe_*.deb)
shopt -u nullglob
if [ "${#debs[@]}" -ne 1 ]; then
  echo "::error::probe-v$pin must carry exactly one hsm-linux-probe_*.deb, found ${#debs[@]}"
  exit 1
fi

deb="${debs[0]}"
sha_file="$deb.sha256"
if [ ! -f "$sha_file" ]; then
  echo "::error::probe-v$pin has no $(basename "$sha_file") next to $(basename "$deb")"
  exit 1
fi

expected="$(cut -d' ' -f1 "$sha_file")"
actual="$(sha256sum "$deb" | cut -d' ' -f1)"
if [ "$expected" != "$actual" ]; then
  echo "::error::sha256 mismatch for probe-v$pin: release says $expected, downloaded file is $actual"
  exit 1
fi

rm "$sha_file"
echo "staged Linux probe $pin: $(basename "$deb") (sha256 $actual)"
ls -l "$dir"
