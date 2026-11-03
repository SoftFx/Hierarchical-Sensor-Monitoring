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

require_bare_dns_name() {
    case "$1" in
        *:*|*[!A-Za-z0-9._-]*) fail "$2" ;;
    esac
}

require_nonblank "${HSM_DOMAIN:-}" 'HSM_DOMAIN is required'
case "$HSM_DOMAIN" in
    \[*\])
        ipv6="${HSM_DOMAIN#\[}"
        ipv6="${ipv6%\]}"
        case "$ipv6" in *[!A-Fa-f0-9:.]*|'') fail 'HSM_DOMAIN must be a bare DNS name or IP address' ;; esac
        ;;
    *) require_bare_dns_name "$HSM_DOMAIN" 'HSM_DOMAIN must be a bare DNS name or IP address' ;;
 esac

# Optional (#1585): write the DNS-01 TXT record at a delegated alias in a stub zone
# (e.g. _acme-challenge.hsm.dev.soft-fx.dev) instead of _acme-challenge.<HSM_DOMAIN>.
# Validated and applied only inside letsencrypt-dns below — outside it the setting is
# ignored, like every other HSM_DNS_* variable.
case "${HSM_CERTIFICATE:-}" in
    letsencrypt|letsencrypt-http)
        HSM_TLS_SNIPPET=tls-letsencrypt-http
        ;;
    letsencrypt-dns)
        # Blank or whitespace-only override means "no override" (the plain snippet); a
        # non-blank value must be a bare DNS name — the leading underscore label of a
        # challenge name is legal, ports and schemes are not.
        override=''
        case "${HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN:-}" in
            *[![:space:]]*)
                require_bare_dns_name "$HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN" \
                    'HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN must be a bare DNS name such as _acme-challenge.alias.example.com'
                override='-override'
                ;;
        esac
        case "${HSM_DNS_PROVIDER:-}" in
            cloudflare)
                require_nonblank "${CF_API_TOKEN:-}" 'CF_API_TOKEN is required for the cloudflare DNS provider'
                HSM_TLS_SNIPPET="tls-dns-cloudflare$override"
                ;;
            dynv6)
                require_nonblank "${DYNV6_API_TOKEN:-}" 'DYNV6_API_TOKEN is required for the dynv6 DNS provider'
                HSM_TLS_SNIPPET="tls-dns-dynv6$override"
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

# Compatibility with customised Caddyfiles copied from a pre-2.11.4-4 image: the docs of
# those releases told operators to keep `import {$HSM_VL_SNIPPET}` in their copies, and
# an unset variable expands to nothing, leaving a bare `import` that Caddy refuses to
# load — taking down every listener Caddy fronts, sensor ingestion included. Those copies
# define their own empty (victorialogs-off) snippet, so pinning the value makes them start
# cleanly with no log routes (fail closed). The log-store routes themselves are gone since
# 2.11.4-6 (ADR-0015): copies merged from 2.11.4-4/-5 carry their own snippet definition
# and import, still load, and answer 502 on /logs/* with no store behind it.
HSM_VL_SNIPPET=victorialogs-off
export HSM_VL_SNIPPET

exec "$@"