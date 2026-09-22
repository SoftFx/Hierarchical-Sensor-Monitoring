#!/usr/bin/env python3
"""The wiki Installation page embeds docker-compose.yml verbatim as the reference setup
(section "Reference docker-compose.yml"), so admins copy the supported HSM + Caddy stack
instead of writing their own (#1411). Fail when the two differ; update them together."""

import difflib
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
COMPOSE = ROOT / "docker-compose.yml"
WIKI = ROOT / "wiki-git" / "Installation.md"
HEADING = "### Reference docker-compose.yml"


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")


def main() -> int:
    for path in (COMPOSE, WIKI):
        if not path.exists():
            print(f"{path.relative_to(ROOT)} not found: if it moved, update this script and compose-wiki-sync.yml.")
            return 1

    compose = read(COMPOSE).rstrip("\n")
    wiki = read(WIKI)

    section = wiki.find(HEADING)
    if section < 0:
        print(f"{WIKI.relative_to(ROOT)} has no '{HEADING}' section.")
        return 1

    # Only the block of this section, not a later one on the page.
    next_heading = min((i for i in (wiki.find("\n## ", section + 1), wiki.find("\n### ", section + 1)) if i >= 0), default=len(wiki))
    start = wiki.find("```yaml\n", section, next_heading)
    end = wiki.find("\n```", start + len("```yaml\n"), next_heading) if start >= 0 else -1
    if start < 0 or end < 0:
        print(f"No yaml block under '{HEADING}' in {WIKI.relative_to(ROOT)}.")
        return 1

    embedded = wiki[start + len("```yaml\n"):end]
    if embedded == compose:
        print("docker-compose.yml and the wiki reference copy are identical.")
        return 0

    print("docker-compose.yml differs from its reference copy in wiki-git/Installation.md; update both together:")
    sys.stdout.writelines(difflib.unified_diff(
        embedded.splitlines(keepends=True), compose.splitlines(keepends=True),
        fromfile="wiki-git/Installation.md (reference block)", tofile="docker-compose.yml"))
    return 1


if __name__ == "__main__":
    sys.exit(main())
