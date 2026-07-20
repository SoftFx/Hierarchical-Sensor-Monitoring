#!/usr/bin/env python3
"""Check that the native collector's version is declared consistently everywhere.

The version lives in four hand-maintained places that must move together; forgetting one publishes a
mismatched package (this already happened once: the vcpkg port and conan recipe sat at 0.4.0 while the
library was 0.6.2). The registry port and version database are NOT checked here - those are rewritten
by the hsm-collector-registry-publish workflow.

    python scripts/check-collector-version.py
    python scripts/check-collector-version.py --tag collector-v0.6.3   # also assert a release tag
    python scripts/check-collector-version.py --root /tmp/extracted    # check another checkout

--root lets the publish workflow point this at an unpacked release tarball, so the CI gate and the
release gate share one definition of "a valid version declaration".
"""
import argparse
import json
import re
import sys
from pathlib import Path


def from_cmake(collector):
    text = (collector / "CMakeLists.txt").read_text(encoding="utf-8")
    m = re.search(r"project\(HsmCollectorNative\s+VERSION\s+(\d+\.\d+\.\d+)", text)
    return m.group(1) if m else None


def from_abi_header(collector):
    text = (collector / "include" / "hsm_collector" / "hsm_collector.h").read_text(encoding="utf-8")
    parts = []
    for component in ("MAJOR", "MINOR", "PATCH"):
        m = re.search(rf"#define\s+HSM_COLLECTOR_VERSION_{component}\s+(\d+)", text)
        if not m:
            return None
        parts.append(m.group(1))
    return ".".join(parts)


def from_overlay_port(collector):
    return json.loads((collector / "vcpkg-port" / "vcpkg.json").read_text(encoding="utf-8")).get("version")


def from_conanfile(collector):
    text = (collector / "conanfile.py").read_text(encoding="utf-8")
    m = re.search(r'^\s*version\s*=\s*"(\d+\.\d+\.\d+)"', text, re.M)
    return m.group(1) if m else None


SOURCES = {
    "CMakeLists.txt (project VERSION)": from_cmake,
    "include/hsm_collector/hsm_collector.h (HSM_COLLECTOR_VERSION_*)": from_abi_header,
    "vcpkg-port/vcpkg.json (overlay port)": from_overlay_port,
    "conanfile.py (conan recipe)": from_conanfile,
}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--tag", help="release tag (collector-vX.Y.Z) that must match the source version")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1],
                        help="repository root to inspect (default: this checkout)")
    args = parser.parse_args()

    collector = args.root / "src" / "native" / "collector"
    if not collector.is_dir():
        print(f"::error::no collector sources under {collector}")
        return 1

    found, unreadable = {}, []
    for label, read in SOURCES.items():
        try:
            value = read(collector)
        except (OSError, ValueError) as ex:
            # ValueError covers json.JSONDecodeError: a corrupt manifest must report cleanly, not
            # crash this gate with a traceback.
            print(f"::error::cannot read {label}: {ex}")
            value = None
        if value:
            found[label] = value
        else:
            unreadable.append(label)

    for label, value in found.items():
        print(f"  {value}\t<- {label}")
    for label in unreadable:
        print(f"::error::could not parse a version from {label}")
    if unreadable:
        return 1

    distinct = sorted(set(found.values()))
    if len(distinct) != 1:
        print(f"::error::collector version is out of sync: {distinct}. Bump every location listed above together.")
        return 1

    version = distinct[0]
    print(f"collector version is consistent: {version}")

    if args.tag:
        expected = args.tag[len("collector-v"):] if args.tag.startswith("collector-v") else args.tag
        if expected != version:
            print(f"::error::tag '{args.tag}' does not match the source version {version} - "
                  f"tag the commit that declares {expected}, or bump the sources first.")
            return 1
        print(f"tag '{args.tag}' matches the source version.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
