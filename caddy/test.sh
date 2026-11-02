#!/bin/sh
set -eu

image="${1:-hsm-caddy:test}"
tmp="$(mktemp -d)"
reload_container="hsm-caddy-reload-$$"
trap 'docker rm -f "$reload_container" >/dev/null 2>&1 || true; rm -rf "$tmp"' EXIT

modules="$(docker run --rm --entrypoint caddy "$image" list-modules)"
printf '%s\n' "$modules" | grep -Fx 'dns.providers.cloudflare' >/dev/null
printf '%s\n' "$modules" | grep -Fx 'dns.providers.dynv6' >/dev/null

adapt_ok() {
    name="$1"
    shift
    output="$tmp/$name.json"
    docker run --rm "$@" "$image" caddy adapt --config /etc/caddy/Caddyfile --adapter caddyfile >"$output"
    grep -F '"persist":false' "$output" >/dev/null
    grep -F '"dial":"app:443"' "$output" >/dev/null
    grep -F '"default_sni":"hsm.example.com"' "$output" >/dev/null
    if grep -F '"logs"' "$output" >/dev/null; then
        echo "$name unexpectedly enables Caddy access logs" >&2
        exit 1
    fi
}

adapt_ok letsencrypt -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt
adapt_ok letsencrypt-http -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-http
cmp "$tmp/letsencrypt.json" "$tmp/letsencrypt-http.json"
grep -F '"module":"acme"' "$tmp/letsencrypt.json" >/dev/null

adapt_ok self-signed -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed
grep -F '"module":"internal"' "$tmp/self-signed.json" >/dev/null

cloudflare_secret='test-cloudflare-secret-not-for-network-use'
adapt_ok dns-cloudflare -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=cloudflare -e "CF_API_TOKEN=$cloudflare_secret"
grep -F '"name":"cloudflare"' "$tmp/dns-cloudflare.json" >/dev/null
grep -F '"api_token":"{env.CF_API_TOKEN}"' "$tmp/dns-cloudflare.json" >/dev/null
if grep -F "$cloudflare_secret" "$tmp/dns-cloudflare.json" >/dev/null; then
    echo 'Cloudflare token leaked into adapted configuration' >&2
    exit 1
fi

dynv6_secret='test-dynv6-secret-not-for-network-use'
adapt_ok dns-dynv6 -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=dynv6 -e "DYNV6_API_TOKEN=$dynv6_secret"
grep -F '"name":"dynv6"' "$tmp/dns-dynv6.json" >/dev/null
grep -F '"token":"{env.DYNV6_API_TOKEN}"' "$tmp/dns-dynv6.json" >/dev/null
if grep -F "$dynv6_secret" "$tmp/dns-dynv6.json" >/dev/null; then
    echo 'dynv6 token leaked into adapted configuration' >&2
    exit 1
fi

# VictoriaLogs read routes (#1580): /logs/select/* is proxied behind forward_auth to the
# app's /logs-auth session check — the HSM admin session is the only credential, so the
# routes exist with no VL_UI_* variables at all. The adapted configuration must carry no
# basic-auth or bcrypt artifacts, and the ingest endpoints (/insert/...) must never be
# routed through Caddy.
adapt_ok vl-routes -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed
grep -F '"dial":"victorialogs:9428"' "$tmp/vl-routes.json" >/dev/null
grep -F '"uri":"/logs-auth"' "$tmp/vl-routes.json" >/dev/null
grep -F '"method":"GET"' "$tmp/vl-routes.json" >/dev/null
# forward_auth's auth subrequest goes to https://app:443, so its proxy must carry the
# same hsm_upstream transport as the main proxies: without the TLS skip every /logs-auth
# call fails on the app's self-signed certificate, and without the Host passthrough a
# login redirect points browsers at the docker-internal name. Both regressions fail
# closed (admins lose /logs/ with no error here), so pin them by count: eleven proxies
# carry the transport today — the single app upstream (#1590) is shared by the primary
# 443 site, the two deprecated-port aliases and the catch-all site's three addresses,
# plus every VictoriaLogs route pairs an auth proxy with its main upstream proxy.
transport_hits="$(grep -oF '"insecure_skip_verify":true' "$tmp/vl-routes.json" | wc -l | tr -d ' ')"
[ "$transport_hits" -ge 11 ] || {
    echo "expected at least 11 proxies with insecure_skip_verify, got $transport_hits" >&2
    exit 1
}
host_hits="$(grep -oF '"Host":["{http.request.hostport}"]' "$tmp/vl-routes.json" | wc -l | tr -d ' ')"
[ "$host_hits" -ge 11 ] || {
    echo "expected at least 11 Host header_up passthroughs, got $host_hits" >&2
    exit 1
}

# Single upstream (#1590): every site — the primary 443 address AND the deprecated
# 44330/44333 aliases still published through the deprecation window — forwards to the
# app's one listener on 443. A per-port upstream here would 502 on one of the surfaces,
# so the legacy dials must be gone while the legacy LISTENERS stay for the window's
# stragglers.
if grep -F '"dial":"app:44330"' "$tmp/vl-routes.json" >/dev/null; then
    echo 'legacy per-port upstream app:44330 survived in the adapted configuration' >&2
    exit 1
fi
if grep -F '"dial":"app:44333"' "$tmp/vl-routes.json" >/dev/null; then
    echo 'legacy per-port upstream app:44333 survived in the adapted configuration' >&2
    exit 1
fi
grep -F '"listen":[":44330"]' "$tmp/vl-routes.json" >/dev/null || {
    echo 'deprecated 44330 alias listener missing from the adapted configuration' >&2
    exit 1
}
grep -F '"listen":[":44333"]' "$tmp/vl-routes.json" >/dev/null || {
    echo 'deprecated 44333 alias listener missing from the adapted configuration' >&2
    exit 1
}
if grep -F '/insert' "$tmp/vl-routes.json" >/dev/null; then
    echo 'VictoriaLogs ingest endpoint routed through Caddy' >&2
    exit 1
fi
if grep -F '"username"' "$tmp/vl-routes.json" >/dev/null; then
    echo 'basic-auth username present in the adapted VictoriaLogs configuration' >&2
    exit 1
fi
if grep -F '$2a$' "$tmp/vl-routes.json" >/dev/null; then
    echo 'bcrypt hash present in the adapted VictoriaLogs configuration' >&2
    exit 1
fi

# Leftover VL_UI_* values from an older .env cannot reach this image through compose
# anymore; passed explicitly, they must be ignored — nothing derived from them (username,
# hash) and not the values themselves may reach the adapted configuration.
vl_legacy_password='leftover-vl-password-not-for-network-use'
adapt_ok vl-legacy-env -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed \
    -e VL_UI_USER=hsm-logs -e "VL_UI_PASSWORD=$vl_legacy_password"
grep -F '"dial":"victorialogs:9428"' "$tmp/vl-legacy-env.json" >/dev/null
if grep -F 'hsm-logs' "$tmp/vl-legacy-env.json" >/dev/null; then
    echo 'ignored VL_UI_USER leaked into the adapted configuration' >&2
    exit 1
fi
if grep -F "$vl_legacy_password" "$tmp/vl-legacy-env.json" >/dev/null; then
    echo 'ignored VL_UI_PASSWORD leaked into the adapted configuration' >&2
    exit 1
fi

# Compatibility (#1580 review): a customised Caddyfile copied from a pre-2.11.4-4 image
# still contains `import {$HSM_VL_SNIPPET}` (the docs of those releases told operators to
# keep it). With the variable no longer exported, the placeholder would expand to a bare
# `import` and Caddy would refuse to load — taking down every listener it fronts, sensor
# ingestion included. Adapting the frozen 2.11.4-3 bundled Caddyfile through this image's
# entrypoint must therefore succeed, keep proxying the app upstreams, and activate neither
# the VictoriaLogs routes nor the basic-auth gate: the entrypoint pins the variable to the
# empty snippet those copies define — fail closed.
old_caddyfile="$(CDPATH= cd -- "$(dirname -- "$0")/testdata" && pwd)/Caddyfile.2.11.4-3"
docker run --rm -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed \
    -v "$old_caddyfile:/etc/caddy/Caddyfile:ro" \
    "$image" caddy adapt --config /etc/caddy/Caddyfile --adapter caddyfile \
    >"$tmp/vl-compat-old-caddyfile.json"
grep -F '"dial":"app:44333"' "$tmp/vl-compat-old-caddyfile.json" >/dev/null
if grep -F '"dial":"victorialogs:9428"' "$tmp/vl-compat-old-caddyfile.json" >/dev/null; then
    echo 'old customised Caddyfile unexpectedly activates the VictoriaLogs routes' >&2
    exit 1
fi
if grep -F '"http_basic"' "$tmp/vl-compat-old-caddyfile.json" >/dev/null; then
    echo 'old customised Caddyfile unexpectedly activates basic auth' >&2
    exit 1
fi

# `caddy validate` loads and provisions the TLS issuer and DNS provider, but does not
# request a certificate. Network isolation keeps these checks offline and prevents DNS/CA calls.
validate_dns_provider() {
    name="$1"
    shift
    docker run --rm --network none "$@" "$image" caddy validate \
        --config /etc/caddy/Caddyfile --adapter caddyfile >"$tmp/$name-validate.out" 2>&1 || {
        cat "$tmp/$name-validate.out" >&2
        echo "$name DNS provider failed runtime provisioning" >&2
        exit 1
    }
    grep -F 'Valid configuration' "$tmp/$name-validate.out" >/dev/null
}

validate_dns_provider provision-cloudflare \
    -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=cloudflare -e CF_API_TOKEN=cfut_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
validate_dns_provider provision-dynv6 \
    -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=dynv6 -e DYNV6_API_TOKEN=offline-test-token

if docker run --rm --network none -e HSM_DOMAIN=hsm.example.com \
    -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=cloudflare \
    -e CF_API_TOKEN=invalid-test-token "$image" caddy validate \
    --config /etc/caddy/Caddyfile --adapter caddyfile >"$tmp/invalid-cloudflare-token.out" 2>&1; then
    echo 'Cloudflare provider accepted a malformed token during runtime provisioning' >&2
    exit 1
fi
grep -F "API token 'invalid-test-token' appears invalid" "$tmp/invalid-cloudflare-token.out" >/dev/null
mkdir "$tmp/certs"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=hsm.example.com' \
    -keyout "$tmp/certs/key.pem" -out "$tmp/certs/cert.pem" >/dev/null 2>&1
adapt_ok custom -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=custom \
    -v "$tmp/certs:/certs:ro"
docker run --rm -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=custom \
    -v "$tmp/certs:/certs:ro" "$image" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null
grep -F '"certificate":"/certs/cert.pem"' "$tmp/custom.json" >/dev/null
grep -F '"key":"/certs/key.pem"' "$tmp/custom.json" >/dev/null
# Exercise the documented restart path with the real image entrypoint and an offline custom cert.
docker run -d --name "$reload_container" --network none \
    -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=custom \
    -v "$tmp/certs:/certs:ro" "$image" >/dev/null
docker restart "$reload_container" >/dev/null
reload_succeeded=false
attempt=1
while [ "$attempt" -le 10 ]; do
    if docker exec "$reload_container" hsm-caddy-entrypoint caddy reload \
        --config /etc/caddy/Caddyfile --adapter caddyfile >"$tmp/reload.out" 2>&1; then
        reload_succeeded=true
        break
    fi
    sleep 1
    attempt=$((attempt + 1))
done
if [ "$reload_succeeded" != true ]; then
    echo 'entrypoint-wrapped Caddy reload did not succeed after restart' >&2
    cat "$tmp/reload.out" >&2
    docker logs "$reload_container" >&2
    exit 1
fi
reject() {
    name="$1"
    expected="$2"
    shift 2
    if docker run --rm "$@" "$image" true >"$tmp/$name.out" 2>&1; then
        echo "$name unexpectedly succeeded" >&2
        exit 1
    fi
    grep -F "$expected" "$tmp/$name.out" >/dev/null
}

reject missing-mode 'HSM_CERTIFICATE is required' -e HSM_DOMAIN=hsm.example.com
reject invalid-mode 'HSM_CERTIFICATE must be' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=automatic
reject missing-provider 'HSM_DNS_PROVIDER is required' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns
reject invalid-provider 'HSM_DNS_PROVIDER must be' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=route53
reject missing-cloudflare-token 'CF_API_TOKEN is required' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=cloudflare
reject missing-dynv6-token 'DYNV6_API_TOKEN is required' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=dynv6
reject blank-cloudflare-token 'CF_API_TOKEN is required' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=cloudflare -e 'CF_API_TOKEN=   '
reject missing-custom-files 'custom mode requires readable /certs/cert.pem' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=custom
reject invalid-domain 'HSM_DOMAIN must be a bare DNS name or IP address' -e 'HSM_DOMAIN=https://hsm.example.com' -e HSM_CERTIFICATE=self-signed
reject domain-with-port 'HSM_DOMAIN must be a bare DNS name or IP address' -e 'HSM_DOMAIN=hsm.example.com:443' -e HSM_CERTIFICATE=self-signed

echo 'hsm-caddy runtime configuration tests passed'