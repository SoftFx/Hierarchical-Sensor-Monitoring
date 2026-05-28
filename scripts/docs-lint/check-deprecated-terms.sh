#!/usr/bin/env bash
# Check curated deprecated terminology in documentation.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

# Pattern ::: description ::: comma-separated file exceptions
RULES=(
  '\bGlobal scheduler\b|\bglobal scheduler\b:::Use `Collector scheduler`:::aicontext/glossary.md'
  '\bTimer task\b|\btimer task\b:::Use `Scheduled task`:::aicontext/glossary.md'
  '\bMetric\b|\bmetric\b:::Use `Sensor` or `sensor value`:::aicontext/glossary.md'
  '\bBackend\b|\bbackend\b:::Use `HSM Server` in public docs:::aicontext/glossary.md'
)

found=0

for rule in "${RULES[@]}"; do
  pattern="${rule%%:::*}"
  rest="${rule#*:::}"
  description="${rest%%:::*}"
  excludes="${rest#*:::}"

  IFS=',' read -ra ex_arr <<< "$excludes"

  while IFS=: read -r file lineno content; do
    [ -z "$file" ] && continue
    skip=0
    for ex in "${ex_arr[@]}"; do
      [ "$file" = "$ex" ] && skip=1 && break
    done
    [ "$skip" -eq 1 ] && continue

    echo "DEPRECATED  $file:$lineno  $description"
    echo "  > $content"
    found=$((found + 1))
  done < <(grep -rnE --include='*.md' "$pattern" AGENTS.md aicontext docs .github 2>/dev/null || true)
done

echo ""
if [ "$found" -gt 0 ]; then
  echo "Deprecated-term occurrences: $found"
  exit 1
fi
echo "Deprecated-term occurrences: 0"
