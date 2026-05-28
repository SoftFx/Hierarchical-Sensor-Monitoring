#!/usr/bin/env bash
# Check feature folders under aicontext/features.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

AREAS=(collector server storage site api integrations)
missing_from_overview=0
missing_feature_md=0

for area in "${AREAS[@]}"; do
  overview="aicontext/features/$area/overview.md"
  if [ ! -f "$overview" ]; then
    echo "MISSING-OVERVIEW  $overview"
    missing_from_overview=$((missing_from_overview + 1))
    continue
  fi

  for d in "aicontext/features/$area"/*/ ; do
    [ -d "$d" ] || continue
    d="${d%/}"
    name="$(basename "$d")"
    [[ "$name" == _* ]] && continue

    if ! grep -q "$name" "$overview"; then
      echo "NOT-IN-OVERVIEW   $d/  (missing from $overview)"
      missing_from_overview=$((missing_from_overview + 1))
    fi

    if [ ! -f "$d/feature.md" ]; then
      echo "NO-FEATURE-MD     $d/"
      missing_feature_md=$((missing_feature_md + 1))
    fi
  done
done

echo ""
errors=$((missing_from_overview + missing_feature_md))
if [ "$errors" -gt 0 ]; then
  echo "Folders missing from overview: $missing_from_overview"
  echo "Folders without feature.md:    $missing_feature_md"
  exit 1
fi
echo "All feature folders are synced with overview docs and have feature.md."
