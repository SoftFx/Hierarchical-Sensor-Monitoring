#!/bin/sh
set -eu

fail() {
    printf 'hsm-caddy: %s\n' "$1" >&2
    exit 1
}

require_nonblank() {
    case "$1" in
        *[![:space:]]*) ;;
        *) fail "$2" ;;
    esac
}

require_nonblank "${HSM_DOMAIN:-}" 'HSM_DOMAIN is required'
case "$HSM_DOMAIN" in
    \[*\])
        ipv6="${HSM_DOMAIN#\[}"
        ipv6="${ipv6%\]}"
        case "$ipv6" in *[!A-Fa-f0-9:.]*|'') fail 'HSM_DOMAIN must be a bare DNS name or IP address' ;; esac
        ;;
    *:*|*[!A-Za-z0-9._-]*) fail 'HSM_DOMAIN must be a bare DNS name or IP address' ;;
 esac

case "${HSM_CERTIFICATE:-}" in
    letsencrypt|letsencrypt-http)
        HSM_TLS_SNIPPET=tls-letsencrypt-http
        ;;
    letsencrypt-dns)
        case "${HSM_DNS_PROVIDER:-}" in
            cloudflare)
                require_nonblank "${CF_API_TOKEN:-}" 'CF_API_TOKEN is required for the cloudflare DNS provider'
                HSM_TLS_SNIPPET=tls-dns-cloudflare
                ;;
            dynv6)
                require_nonblank "${DYNV6_API_TOKEN:-}" 'DYNV6_API_TOKEN is required for the dynv6 DNS provider'
                HSM_TLS_SNIPPET=tls-dns-dynv6
                ;;
            '') fail 'HSM_DNS_PROVIDER is required for letsencrypt-dns' ;;
            *) fail 'HSM_DNS_PROVIDER must be cloudflare or dynv6' ;;
        esac
        ;;
    self-signed)
        HSM_TLS_SNIPPET=tls-self-signed
        ;;
    custom)
        [ -r /certs/cert.pem ] || fail 'custom mode requires readable /certs/cert.pem'
        [ -r /certs/key.pem ] || fail 'custom mode requires readable /certs/key.pem'
        HSM_TLS_SNIPPET=tls-custom
        ;;
    '') fail 'HSM_CERTIFICATE is required' ;;
    *) fail 'HSM_CERTIFICATE must be letsencrypt, letsencrypt-http, letsencrypt-dns, self-signed, or custom' ;;
 esac

export HSM_TLS_SNIPPET

# VictoriaLogs read-only exposure (/select/vmui and /select/logsql, docker-compose 'logs'
# profile): enabled only when credentials are configured. The bcrypt hash is generated here
# and passed to Caddy through the environment, like the TLS snippet; the plaintext password
# never reaches the Caddyfile or the adapted configuration.
if [ -n "${VL_UI_USER:-}" ] || [ -n "${VL_UI_PASSWORD:-}" ]; then
    require_nonblank "${VL_UI_USER:-}" 'VL_UI_USER is required for the VictoriaLogs UI when VL_UI_PASSWORD is set'
    require_nonblank "${VL_UI_PASSWORD:-}" 'VL_UI_PASSWORD is required for the VictoriaLogs UI when VL_UI_USER is set'
    # Allow-list, not a block-list: the username lands verbatim in the Caddyfile's
    # basic_auth line, where a leading '#', a quote, or a backslash breaks tokenization
    # with a confusing Caddy error instead of this clear one.
    case "$VL_UI_USER" in
        *[!A-Za-z0-9._@-]*) fail 'VL_UI_USER must contain only letters, digits, and . _ @ - characters' ;;
    esac
    # Refuse the published placeholder and short passwords outright: these routes sit on the
    # public listeners, so a guessable credential exposes every shipped log line. A fresh
    # install ships the credentials commented out (.env.example); a deploy that re-enables
    # the placeholder gets a hard error naming the variable, not a public hole.
    case "$(printf '%s' "$VL_UI_PASSWORD" | tr 'A-Z' 'a-z')" in
        change-me) fail 'VL_UI_PASSWORD must be changed from the example placeholder before the log UI is exposed' ;;
    esac
    if [ "${#VL_UI_PASSWORD}" -lt 12 ]; then
        fail 'VL_UI_PASSWORD must be at least 12 characters'
    fi
    # bcrypt cost 10, not Caddy's default 14: this Caddy also fronts sensor ingestion, and
    # every failed basic-auth attempt pays the full bcrypt cost (only successful checks are
    # cached), so the default would let unauthenticated traffic burn CPU and degrade
    # ingestion. Cost 10 is tens of milliseconds; the compensating control is the required
    # 12+ character random password.
    # The password goes in on stdin, not as a --plaintext argument: an argument is
    # visible in /proc/<pid>/cmdline (docker top, ps) while the hash is computed.
    VL_UI_BCRYPT_HASH="$(printf '%s\n' "$VL_UI_PASSWORD" | caddy hash-password --algorithm bcrypt --bcrypt-cost 10)"
    require_nonblank "${VL_UI_BCRYPT_HASH}" 'caddy hash-password produced no bcrypt hash'
    HSM_VL_SNIPPET=victorialogs-routes
else
    HSM_VL_SNIPPET=victorialogs-off
fi

export HSM_VL_SNIPPET VL_UI_BCRYPT_HASH
exec "$@"