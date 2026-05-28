#!/usr/bin/env bash
# Check local markdown links in repo documentation.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

mapfile -t FILES < <(
  {
    printf '%s\n' "AGENTS.md" "README.md" "ReleaseNote.md" "QWEN.md" "WORK_SESSION_NOTES.md"
    find aicontext docs .github -type f -name '*.md' 2>/dev/null
  } | sort -u
)

broken=0
total=0

exact_case_exists() {
  local path="$1"
  local rel current part entry found

  rel="$(realpath -m --relative-to="$REPO_ROOT" "$path" 2>/dev/null || true)"
  [ -z "$rel" ] && return 1
  [[ "$rel" == ..* ]] && return 0

  current="$REPO_ROOT"
  IFS='/' read -ra parts <<< "$rel"

  shopt -s nullglob dotglob
  for part in "${parts[@]}"; do
    [ -z "$part" ] || [ "$part" = "." ] && continue
    found=""
    for entry in "$current"/*; do
      if [ "$(basename "$entry")" = "$part" ]; then
        found="$entry"
        break
      fi
    done
    if [ -z "$found" ]; then
      shopt -u nullglob dotglob
      return 1
    fi
    current="$found"
  done
  shopt -u nullglob dotglob
  return 0
}

for f in "${FILES[@]}"; do
  [ -f "$f" ] || continue
  base="$(basename "$f")"
  [[ "$base" == _TEMPLATE* ]] && continue
  dir="$(dirname "$f")"

  while IFS= read -r target; do
    [ -z "$target" ] && continue
    [[ "$target" == http://* || "$target" == https://* || "$target" == mailto:* || "$target" == \#* ]] && continue
    [[ "$target" == *"{"*"}"* || "$target" == *"<"*">"* || "$target" == *"*"* ]] && continue

    clean="${target%%#*}"
    clean="${clean%%\?*}"
    [ -z "$clean" ] && continue

    total=$((total + 1))
    resolved="$(cd "$dir" && realpath -m "$clean" 2>/dev/null || true)"
    if [ ! -e "$resolved" ]; then
      echo "BROKEN  $f  ->  $target"
      broken=$((broken + 1))
    elif ! exact_case_exists "$resolved"; then
      echo "CASE-MISMATCH  $f  ->  $target"
      broken=$((broken + 1))
    fi
  done < <(perl -ne 'while (/\]\(([^)]+)\)/g) { print "$1\n"; }' "$f")
done

echo ""
echo "Links checked: $total"
if [ "$broken" -gt 0 ]; then
  echo "Broken links:  $broken"
  exit 1
fi
echo "Broken links:  0"
