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
exec "$@"