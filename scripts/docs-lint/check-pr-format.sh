#!/usr/bin/env bash
# Generic GitHub PR body checker.

set -uo pipefail

err=0
PR_TITLE="${PR_TITLE:-}"
PR_BODY="${PR_BODY:-}"

if [ -z "$PR_TITLE" ]; then
  echo "::error::PR_TITLE env is empty"
  exit 1
fi
if [ -z "$PR_BODY" ]; then
  echo "::error::PR_BODY env is empty"
  exit 1
fi

PR_BODY="$(perl -0777 -pe 's/<!--.*?-->//gs' <<< "$PR_BODY")"

if [ "${#PR_TITLE}" -lt 8 ]; then
  echo "::error::PR title is too short to be useful"
  err=$((err + 1))
fi

require_header() {
  local header="$1"
  if ! grep -qE "^##[[:space:]]+${header}[[:space:]]*$" <<< "$PR_BODY"; then
    echo "::error::Missing required '## ${header}' section"
    err=$((err + 1))
    return 1
  fi
  return 0
}

extract_block() {
  local header="$1"
  awk -v h="$header" '
    $0 ~ "^##[[:space:]]+"h"[[:space:]]*$" {capture=1; next}
    capture && /^##[[:space:]]/ {capture=0}
    capture {print}
  ' <<< "$PR_BODY" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
}

is_block_empty() {
  local header="$1"
  local block
  block="$(extract_block "$header" | tr -d '[:space:]')"
  [ -z "$block" ] && return 0
  case "$block" in
    TBD|tbd|pending|Pending|na|NA|"—"|"-") return 0 ;;
  esac
  return 1
}

for section in "Summary" "What Changed" "Tests" "Risks / Follow-Up"; do
  require_header "$section"
done

for section in "Summary" "Tests"; do
  if is_block_empty "$section"; then
    echo "::error::'## ${section}' is empty"
    err=$((err + 1))
  fi
done

if grep -qE '^##[[:space:]]+Review Result[[:space:]]*$' <<< "$PR_BODY"; then
  if is_block_empty "Review Result"; then
    echo "::warning::'## Review Result' is present but empty"
  fi
fi

echo ""
echo "Errors: $err"
if [ "$err" -gt 0 ]; then
  echo "Fix: use the generic HSM PR body sections from AGENTS.md / task-branch-pr-workflow.md."
  exit 1
fi
echo "PR format OK."
