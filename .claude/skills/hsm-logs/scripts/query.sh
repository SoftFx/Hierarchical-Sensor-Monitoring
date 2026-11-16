#!/usr/bin/env bash
# Run a LogsQL query against the HSM server logs in the central VictoriaLogs.
#
# usage: query.sh [-s START] [-e END] [-l LIMIT] [-t ACCOUNT:PROJECT] '<LogsQL>'
#   START/END: RFC3339 (2026-11-16T15:00:00Z) or a relative age (15m, 6h, 2d); default START=1h, END=now
#   LIMIT:     max records returned (default 1000); stats queries return few rows anyway
#   TENANT:    VictoriaLogs tenant, default 0:1 (the HSM production server)
#
# Settings: TTS_LOGS_URL (log server address), TTS_LOGS_API_USER (Authentik username) and
# TTS_LOGS_API_PASSWORD (its App password) from the environment or from the project .env (also
# found when running inside a git worktree). Values are never printed.
set -euo pipefail

start="1h"; end=""; limit=1000; tenant="0:1"
while getopts "s:e:l:t:h" opt; do
  case "$opt" in
    s) start="$OPTARG" ;;
    e) end="$OPTARG" ;;
    l) limit="$OPTARG" ;;
    t) tenant="$OPTARG" ;;
    *) sed -n '2,11p' "$0"; exit 2 ;;
  esac
done
shift $((OPTIND - 1))
[ $# -eq 1 ] || { sed -n '2,11p' "$0"; exit 2; }
query="$1"

[[ "$tenant" =~ ^([0-9]+):([0-9]+)$ ]] || { echo "tenant must be ACCOUNT:PROJECT, e.g. 0:1" >&2; exit 2; }
account="${BASH_REMATCH[1]}"; project="${BASH_REMATCH[2]}"

load_env() {
  local f="$1"
  [ -f "$f" ] || return 1
  # Only the three TTS_LOGS_* keys are taken from the file.
  while IFS='=' read -r k v; do
    case "$k" in
      TTS_LOGS_URL|TTS_LOGS_API_USER|TTS_LOGS_API_PASSWORD)
        v="${v%$'\r'}"; v="${v%\"}"; v="${v#\"}"
        [ -n "${!k:-}" ] || export "$k=$v" ;;
    esac
  done < "$f"
}

if [ -z "${TTS_LOGS_API_USER:-}" ] || [ -z "${TTS_LOGS_API_PASSWORD:-}" ]; then
  load_env ".env" || true
  top=$(git rev-parse --show-toplevel 2>/dev/null || true)
  [ -n "$top" ] && load_env "$top/.env" || true
  # In a worktree the .env lives in the main checkout (first entry of the worktree list).
  main=$(git worktree list --porcelain 2>/dev/null | sed -n '1s/^worktree //p' || true)
  [ -n "$main" ] && load_env "$main/.env" || true
fi
for k in TTS_LOGS_URL TTS_LOGS_API_USER TTS_LOGS_API_PASSWORD; do
  [ -n "${!k:-}" ] || { echo "missing $k in .env: see .claude/skills/hsm-logs/SKILL.md" >&2; exit 3; }
done

# Relative age -> RFC3339 (VictoriaLogs also accepts it, but explicit times make the output reproducible)
to_time() {
  case "$1" in
    *[0-9][smhd]) local n="${1%?}" u="${1: -1}"
                  case "$u" in s) u=seconds ;; m) u=minutes ;; h) u=hours ;; d) u=days ;; esac
                  date -u -d "-$n $u" +%Y-%m-%dT%H:%M:%SZ ;;
    *) echo "$1" ;;
  esac
}

args=(--data-urlencode "query=$query" --data-urlencode "start=$(to_time "$start")" --data-urlencode "limit=$limit")
[ -n "$end" ] && args+=(--data-urlencode "end=$(to_time "$end")")

# Credentials go to curl through a config on stdin, so they never show up in the process list.
# Inside a quoted curl config value \ and " are escapes, so they are escaped here.
cfg_escape() { local s="${1//\\/\\\\}"; printf '%s' "${s//\"/\\\"}"; }
printf 'user = "%s:%s"\n' "$(cfg_escape "$TTS_LOGS_API_USER")" "$(cfg_escape "$TTS_LOGS_API_PASSWORD")" |
  curl -sS --fail-with-body -K - -H "AccountID: $account" -H "ProjectID: $project" \
    "${TTS_LOGS_URL%/}/select/logsql/query" "${args[@]}"
