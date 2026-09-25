#!/usr/bin/env python3
"""Keep the published HSM Caddy tag immutable and aligned with the reference compose."""

from __future__ import annotations

import argparse
import pathlib
import re
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent


def release_coordinates(root: pathlib.Path = ROOT) -> tuple[str, str]:
    workflow = (root / ".github/workflows/caddy-image.yml").read_text(encoding="utf-8")
    compose = (root / "docker-compose.yml").read_text(encoding="utf-8")
    image_match = re.search(r"^  IMAGE: ([^\s]+)$", workflow, re.MULTILINE)
    version_match = re.search(r"^  VERSION: ([^\s]+)$", workflow, re.MULTILINE)
    if not image_match or not version_match:
        raise ValueError("caddy-image.yml must define literal IMAGE and VERSION values")
    image, version = image_match.group(1), version_match.group(1)
    if f"image: '{image}:{version}'" not in compose:
        raise ValueError(f"docker-compose.yml must reference {image}:{version}")
    return image, version


def assert_tag_absent(image: str, version: str) -> None:
    namespace, repository = image.split("/", 1)
    url = f"https://hub.docker.com/v2/repositories/{namespace}/{repository}/tags/{version}"
    request = urllib.request.Request(url, headers={"User-Agent": "hsm-caddy-release-guard"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            status = response.status
    except urllib.error.HTTPError as error:
        if error.code == 404:
            print(f"Docker Hub tag is available: {image}:{version}")
            return
        raise RuntimeError(f"Docker Hub returned HTTP {error.code}; refusing to publish") from error
    except urllib.error.URLError as error:
        raise RuntimeError(f"Docker Hub lookup failed; refusing to publish: {error.reason}") from error
    if status == 200:
        raise RuntimeError(f"immutable Docker Hub tag already exists: {image}:{version}")
    raise RuntimeError(f"Docker Hub returned unexpected HTTP {status}; refusing to publish")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-registry", action="store_true")
    args = parser.parse_args()
    try:
        image, version = release_coordinates()
        print(f"Reference compose and workflow agree on {image}:{version}")
        if args.check_registry:
            assert_tag_absent(image, version)
    except (OSError, ValueError, RuntimeError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
