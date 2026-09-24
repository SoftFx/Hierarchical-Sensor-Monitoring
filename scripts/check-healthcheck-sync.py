#!/usr/bin/env python3
"""The HSM server image carries a HEALTHCHECK (#1465). docker-compose.yml repeats it verbatim,
so the bundled stack also works with an image published before #1465 — `depends_on:
condition: service_healthy` refuses to start caddy when neither the image nor the compose file
defines a check. Two copies may not drift: fail when they differ, and change them together.

Stdlib only (no PyYAML): the compose block is read the way check-compose-wiki-sync.py reads its
section, by locating the lines instead of parsing the document."""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
COMPOSE = ROOT / "docker-compose.yml"
DOCKERFILE = ROOT / "docker_scripts" / "HSMserver" / "Dockerfile.healthcheck"

# Dockerfile flag -> compose key, in the order the report prints them.
FIELDS = (("interval", "interval"), ("timeout", "timeout"), ("start-period", "start_period"), ("retries", "retries"))


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")


def parse_dockerfile(text: str) -> dict:
    """The HEALTHCHECK instruction: --flags plus the command after CMD, backslash-continued."""
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if not line.startswith("HEALTHCHECK"):
            continue
        instruction = line
        while instruction.endswith("\\"):
            i += 1
            if i >= len(lines):
                break
            instruction = instruction[:-1].rstrip() + " " + lines[i].strip()
        parsed = {}
        for flag, key in FIELDS:
            match = re.search(rf"--{flag}=(\S+)", instruction)
            if match:
                parsed[key] = match.group(1)
        command = re.search(r"\sCMD\s+(.+)$", instruction)
        if not command:
            return {}
        parsed["test"] = command.group(1).strip()
        return parsed
    return {}


def parse_compose(text: str) -> dict:
    """The healthcheck: block of the app service, as written (flow-sequence test, scalar rest)."""
    lines = text.split("\n")
    try:
        start = next(i for i, line in enumerate(lines) if line.strip() == "healthcheck:")
    except StopIteration:
        return {}
    indent = len(lines[start]) - len(lines[start].lstrip())
    parsed = {}
    for line in lines[start + 1:]:
        if line.strip() and (len(line) - len(line.lstrip())) <= indent:
            break  # back to the service's own keys
        stripped = line.strip()
        if stripped.startswith("#") or ":" not in stripped:
            continue
        key, value = stripped.split(":", 1)
        value = value.strip()
        if key == "test":
            # test: ['CMD-SHELL', '<command>'] — take the last quoted element.
            elements = re.findall(r"'((?:[^']|'')*)'|\"([^\"]*)\"", value)
            if not elements:
                return {}
            parsed["test"] = (elements[-1][0] or elements[-1][1]).replace("''", "'")
        elif key in {"interval", "timeout", "start_period", "retries"}:
            parsed[key] = value
    return parsed


def main() -> int:
    for path in (COMPOSE, DOCKERFILE):
        if not path.exists():
            print(f"{path.relative_to(ROOT)} not found: if it moved, update this script and its workflow.")
            return 1

    image = parse_dockerfile(read(DOCKERFILE))
    compose = parse_compose(read(COMPOSE))
    if not image.get("test"):
        print(f"No HEALTHCHECK instruction found in {DOCKERFILE.relative_to(ROOT)}.")
        return 1
    if not compose.get("test"):
        print(f"No healthcheck: block with a test: found in {COMPOSE.relative_to(ROOT)}. It must repeat "
              "the image's check, or the bundled stack breaks on an image published before #1465.")
        return 1

    # The Dockerfile's shell form becomes ["CMD-SHELL", "<command>"] in the image config, which is
    # exactly what compose's test: ['CMD-SHELL', '<command>'] sends, so the strings must match.
    differences = [(key, image.get(key), compose.get(key))
                   for key in ("test", "interval", "timeout", "start_period", "retries")
                   if image.get(key) != compose.get(key)]
    if not differences:
        print("docker-compose.yml repeats the image HEALTHCHECK exactly.")
        return 0

    print("The compose healthcheck and the image HEALTHCHECK differ; update both together:")
    for key, in_image, in_compose in differences:
        print(f"  {key}:\n    Dockerfile.healthcheck: {in_image}\n    docker-compose.yml:     {in_compose}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
