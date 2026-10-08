#!/usr/bin/env python3
"""Keep the published HSM Caddy tag immutable and aligned with the reference compose."""

from __future__ import annotations

import argparse
import os
import pathlib
import re
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
# The docker build context of the image; a change here changes the published bytes.
IMAGE_CONTEXT = "caddy"


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


def tag_published(image: str, version: str) -> bool:
    namespace, repository = image.split("/", 1)
    url = f"https://hub.docker.com/v2/repositories/{namespace}/{repository}/tags/{version}"
    request = urllib.request.Request(url, headers={"User-Agent": "hsm-caddy-release-guard"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            status = response.status
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return False
        raise RuntimeError(f"Docker Hub returned HTTP {error.code}; refusing to publish") from error
    except urllib.error.URLError as error:
        raise RuntimeError(f"Docker Hub lookup failed; refusing to publish: {error.reason}") from error
    if status == 200:
        return True
    raise RuntimeError(f"Docker Hub returned unexpected HTTP {status}; refusing to publish")


def assert_tag_absent(image: str, version: str) -> None:
    if tag_published(image, version):
        raise RuntimeError(f"immutable Docker Hub tag already exists: {image}:{version}")
    print(f"Docker Hub tag is available: {image}:{version}")


def git(root: pathlib.Path, *args: str) -> subprocess.CompletedProcess:
    # Generous timeout: in a treeless checkout, pickaxe fetches the blobs it compares lazily.
    return subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, timeout=300)


def first_parent_commits(root: pathlib.Path, *args: str) -> list[str]:
    # Merges are diffed against their first parent explicitly; older git skips them in pickaxe.
    result = git(root, "log", "--first-parent", "--diff-merges=first-parent", "--no-patch", "--format=%H", *args)
    commits = result.stdout.split()
    if result.returncode != 0 or not commits:
        raise RuntimeError(f"git log {' '.join(args)} found no commit; refusing to decide (is the checkout shallow?)")
    return commits


def unbumped_context_change(version: str, root: pathlib.Path = ROOT) -> bool:
    """Whether caddy/ changed after the current VERSION value first appeared on the first-parent history.

    Works on merges, so a PR that sets VERSION and edits caddy/ in any commit order counts as bumped,
    and the answer does not depend on which earlier runs succeeded. Taking the first appearance of
    the value means returning VERSION to an older, already published tag is not a bump.
    """
    shallow = git(root, "rev-parse", "--is-shallow-repository")
    if shallow.returncode != 0 or shallow.stdout.strip() != "false":
        # A shallow boundary commit appears to add every file and would make any change look bumped.
        raise RuntimeError("the checkout is shallow or not a repository; refusing to decide")
    context = first_parent_commits(root, "-1", "--", IMAGE_CONTEXT)[0]
    if not re.fullmatch(r"[A-Za-z0-9._-]+", version):
        raise ValueError(f"unexpected VERSION value: {version}")
    escaped = version.replace(".", "[.]")
    introduced = first_parent_commits(
        root, "--pickaxe-regex", "-S", f"^  VERSION: {escaped}$", "--", ".github/workflows/caddy-image.yml"
    )[-1]
    ancestor = git(root, "merge-base", "--is-ancestor", context, introduced)
    if ancestor.returncode not in (0, 1):
        raise RuntimeError(f"git merge-base failed ({ancestor.returncode}); refusing to decide")
    return ancestor.returncode == 1


def plan_publish(image: str, version: str, published: bool, unbumped_change: bool) -> bool:
    if not published:
        print(f"{image}:{version} is not on Docker Hub yet; a master run publishes it")
        return True
    if unbumped_change:
        raise RuntimeError(
            f"{IMAGE_CONTEXT}/ changed after {image}:{version} was introduced and that tag is already published; "
            "bump VERSION in caddy-image.yml and docker-compose.yml"
        )
    print(f"::notice::{image}:{version} is already published and {IMAGE_CONTEXT}/ has not changed since; "
          "skipping publication")
    return False


def write_output(name: str, value: str) -> None:
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as output:
            output.write(f"{name}={value}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-registry", action="store_true")
    parser.add_argument("--plan", action="store_true", help="decide whether this run publishes; sets output 'publish'")
    parser.add_argument("--no-publish", action="store_true", help="with --plan: run the checks but never publish (PRs)")
    args = parser.parse_args()
    try:
        image, version = release_coordinates()
        print(f"Reference compose and workflow agree on {image}:{version}")
        if args.plan:
            published = tag_published(image, version)
            unbumped = unbumped_context_change(version) if published else False
            publish = plan_publish(image, version, published, unbumped) and not args.no_publish
            write_output("publish", "true" if publish else "false")
        if args.check_registry:
            assert_tag_absent(image, version)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
