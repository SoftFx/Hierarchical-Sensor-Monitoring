#!/usr/bin/env bash
# Run a LogsQL query against the HSM server logs in the central VictoriaLogs.
#
# usage: query.sh [-E prod|staging] [-s START] [-e END] [-l LIMIT] [-t ACCOUNT:PROJECT] '<LogsQL>'
#   ENV:       which log server, default prod (tts-logs); staging is the staging log server
#   START/END: RFC3339 (2026-11-16T15:00:00Z) or a relative age (15m, 6h, 2d); default START=1h, END=now
#   LIMIT:     max records returned (default 100); stats queries return few rows anyway
#   TENANT:    VictoriaLogs tenant, default 0:1 (HSM server logs on both log servers)
#
# Settings: TTS_LOGS_URL (log server address), TTS_LOGS_API_USER (Authentik username) and
# TTS_LOGS_API_PASSWORD (its App password); for -E staging the same keys with the TTS_LOGS_STAGING_
# prefix. Read from the environment or from the project .env (also found when running inside a git
# worktree). Values are never printed.
set -euo pipefail

usage() { sed -n '2,13p' "$0"; exit 2; }

env_name="prod"; start="1h"; end=""; limit=100; tenant="0:1"
while getopts "E:s:e:l:t:h" opt; do
  case "$opt" in
    E) env_name="$OPTARG" ;;
    s) start="$OPTARG" ;;
    e) end="$OPTARG" ;;
    l) limit="$OPTARG" ;;
    t) tenant="$OPTARG" ;;
    *) usage ;;
  esac
done
shift $((OPTIND - 1))
[ $# -eq 1 ] || usage
query="$1"

[[ "$tenant" =~ ^([0-9]+):([0-9]+)$ ]] || { echo "tenant must be ACCOUNT:PROJECT, e.g. 0:1" >&2; exit 2; }
account="${BASH_REMATCH[1]}"; project="${BASH_REMATCH[2]}"

case "$env_name" in
  prod)    prefix="TTS_LOGS_" ;;
  staging) prefix="TTS_LOGS_STAGING_" ;;
  *) echo "-E must be prod or staging" >&2; exit 2 ;;
esac
URL_KEY="${prefix}URL"; USER_KEY="${prefix}API_USER"; PASSWORD_KEY="${prefix}API_PASSWORD"
KEYS=("$URL_KEY" "$USER_KEY" "$PASSWORD_KEY")

# Takes the three keys of the chosen log server from one .env file; a value already in the environment wins.
# Splits at the first '=', reads an unterminated last line, accepts 'export ' and quoted values.
load_env() {
  local line k v
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"; line="${line#"${line%%[![:space:]]*}"}"; line="${line#export }"
    [[ "$line" == *=* ]] || continue
    k="${line%%=*}"; k="${k%"${k##*[![:space:]]}"}"
    v="${line#*=}"; v="${v#"${v%%[![:space:]]*}"}"; v="${v%"${v##*[![:space:]]}"}"
    case "$v" in
      \"*\"|\'*\') v="${v:1:${#v}-2}" ;;
      *) v="${v%%[[:space:]]#*}" ;;   # unquoted: ' #...' is a comment
    esac
    if [ "$k" = "$URL_KEY" ] || [ "$k" = "$USER_KEY" ] || [ "$k" = "$PASSWORD_KEY" ]; then
      [ -n "${!k:-}" ] || export "$k=$v"
    fi
  done < "$1"
}

missing() { local k; for k in "${KEYS[@]}"; do [ -n "${!k:-}" ] || return 0; done; return 1; }

if missing; then
  # The repo .env, or in a worktree the main checkout's (first entry of the worktree list). All keys
  # come from the first file that holds the password, so a URL and credentials never mix across files.
  top=$(git rev-parse --show-toplevel 2>/dev/null || true)
  main=$(git worktree list --porcelain 2>/dev/null | sed -n '1s/^worktree //p' || true)
  for d in "$top" "$main"; do
    [ -n "$d" ] && [ -f "$d/.env" ] || continue
    grep -Eq "^[[:space:]]*(export[[:space:]]+)?${PASSWORD_KEY}[[:space:]]*=" "$d/.env" || continue
    load_env "$d/.env"; break
  done
fi
for k in "${KEYS[@]}"; do
  [ -n "${!k:-}" ] || { echo "missing $k in .env: see .claude/skills/hsm-logs/SKILL.md" >&2; exit 3; }
done
url="${!URL_KEY}"
[[ "$url" == https://* ]] || { echo "$URL_KEY must start with https://" >&2; exit 3; }

# Relative age -> RFC3339 (VictoriaLogs also accepts it, but explicit times make the output reproducible).
# Without GNU date (macOS) the relative age goes to VictoriaLogs as is.
to_time() {
  case "$1" in
    *[0-9][smhd]) local n="${1%?}" u="${1: -1}"
                  case "$u" in s) u=seconds ;; m) u=minutes ;; h) u=hours ;; d) u=days ;; esac
                  date -u -d "-$n $u" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || echo "$1" ;;
    *) echo "$1" ;;
  esac
}

args=(--data-urlencode "query=$query" --data-urlencode "start=$(to_time "$start")" --data-urlencode "limit=$limit")
[ -n "$end" ] && args+=(--data-urlencode "end=$(to_time "$end")")

# Credentials go to curl through a config on stdin, so they never show up in the process list.
# Inside a quoted curl config value \ and " are escapes, so they are escaped here.
cfg_escape() { local s="${1//\\/\\\\}"; printf '%s' "${s//\"/\\\"}"; }
printf 'user = "%s:%s"\n' "$(cfg_escape "${!USER_KEY}")" "$(cfg_escape "${!PASSWORD_KEY}")" |
  curl -sS --fail-with-body -K - -H "AccountID: $account" -H "ProjectID: $project" \
    "${url%/}/select/logsql/query" "${args[@]}"
