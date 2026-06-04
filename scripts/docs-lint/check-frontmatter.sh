#!/usr/bin/env bash
# Check canonical documentation frontmatter.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

FILES=(
  "AGENTS.md"
  "aicontext/README.md"
  "aicontext/glossary.md"
  "aicontext/features/_TEMPLATE_feature.md"
  "aicontext/features/collector/overview.md"
  "aicontext/features/server/overview.md"
  "aicontext/features/storage/overview.md"
  "aicontext/features/site/overview.md"
  "aicontext/features/api/overview.md"
  "aicontext/features/integrations/overview.md"
  "aicontext/architecture/docker.md"
  "aicontext/architecture/logging.md"
  "aicontext/architecture/task-branch-pr-workflow.md"
  "aicontext/architecture/testing.md"
  "aicontext/architecture/testing-index.md"
  "aicontext/architecture/development_lifecycle/index.md"
  "aicontext/architecture/development_lifecycle/technical_review_orchestration.md"
  "aicontext/architecture/development_lifecycle/review_roles/index.md"
  "aicontext/flows/README.md"
  "docs/decisions/INDEX.md"
)

TEMPLATE_FILES=(
  "aicontext/features/_TEMPLATE_feature.md"
)

is_template() {
  for t in "${TEMPLATE_FILES[@]}"; do
    [ "$1" = "$t" ] && return 0
  done
  return 1
}

bad=0
missing=0
OWNER_RX='(collector|server|storage|site|api|integrations|shared|docs|testing|operations)'

for f in "${FILES[@]}"; do
  if [ ! -f "$f" ]; then
    echo "MISSING-FILE  $f"
    missing=$((missing + 1))
    continue
  fi

  if is_template "$f"; then
    if ! head -10 "$f" | grep -qE '^> Owner: .+ \| Last reviewed: .+ \| Canonical: yes'; then
      echo "BAD-FRONTMATTER  $f  (template)"
      bad=$((bad + 1))
    fi
  else
    if ! head -10 "$f" | grep -qE "^> Owner: ${OWNER_RX}([[:space:]]+\(.+\))? \| Last reviewed: [0-9]{4}-[0-9]{2}-[0-9]{2} \| Canonical: yes"; then
      echo "BAD-FRONTMATTER  $f"
      bad=$((bad + 1))
    fi
  fi
done

echo ""
echo "Canonical files checked: ${#FILES[@]}"
if [ "$bad" -gt 0 ] || [ "$missing" -gt 0 ]; then
  echo "Bad frontmatter: $bad"
  echo "Missing files:   $missing"
  exit 1
fi
echo "All frontmatter OK."
