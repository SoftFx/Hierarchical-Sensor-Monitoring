#!/bin/sh
set -eu

image="${1:-hsm-caddy:test}"
tmp="$(mktemp -d)"
reload_container="hsm-caddy-reload-$$"
archive_network="hsm-caddy-archives-$$"
archive_upstream="hsm-caddy-upstream-$$"
archive_proxy="hsm-caddy-proxy-$$"
archive_client="hsm-caddy-client-$$"
cleanup() {
    docker rm -f "$reload_container" "$archive_upstream" "$archive_proxy" "$archive_client" >/dev/null 2>&1 || true
    docker network rm "$archive_network" >/dev/null 2>&1 || true
    rm -rf "$tmp"
}
trap cleanup EXIT

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

# The stub-zone override (#1585): a non-empty HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN makes the
# entrypoint select the -override snippet variant, which points the DNS-01 challenge at the
# delegated alias instead of _acme-challenge.<HSM_DOMAIN>. Applies to both providers: the
# directive belongs to the ACME issuer, not to the DNS provider module.
override_domain='_acme-challenge.hsm.alias.example.dev'
adapt_ok dns-cloudflare-override -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=cloudflare -e "CF_API_TOKEN=$cloudflare_secret" \
    -e "HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=$override_domain"
grep -F '"override_domain":"_acme-challenge.hsm.alias.example.dev"' "$tmp/dns-cloudflare-override.json" >/dev/null
grep -F '"name":"cloudflare"' "$tmp/dns-cloudflare-override.json" >/dev/null
if grep -F "$cloudflare_secret" "$tmp/dns-cloudflare-override.json" >/dev/null; then
    echo 'Cloudflare token leaked into adapted configuration' >&2
    exit 1
fi

adapt_ok dns-dynv6-override -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=dynv6 -e "DYNV6_API_TOKEN=$dynv6_secret" \
    -e "HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=$override_domain"
grep -F '"override_domain":"_acme-challenge.hsm.alias.example.dev"' "$tmp/dns-dynv6-override.json" >/dev/null

# Without the override set, neither plain DNS adaptation may carry an override domain.
if grep -F '"override_domain"' "$tmp/dns-cloudflare.json" >/dev/null; then
    echo 'override domain leaked into the plain cloudflare adaptation' >&2
    exit 1
fi
if grep -F '"override_domain"' "$tmp/dns-dynv6.json" >/dev/null; then
    echo 'override domain leaked into the plain dynv6 adaptation' >&2
    exit 1
fi

# A whitespace-only value means "no override": the plain snippet is selected. Compose always
# passes the variable through (.env ships it empty), so the unset case never occurs in practice.
adapt_ok blank-override-is-off -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=cloudflare -e "CF_API_TOKEN=$cloudflare_secret" \
    -e 'HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=   '
cmp "$tmp/blank-override-is-off.json" "$tmp/dns-cloudflare.json"

# The override applies only to letsencrypt-dns: with another certificate mode the value is
# ignored exactly like a stray provider setting, and startup must not fail.
adapt_ok override-ignored-off-dns -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed \
    -e "HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=$override_domain"
cmp "$tmp/override-ignored-off-dns.json" "$tmp/self-signed.json"

# The VictoriaLogs log-store stack is retired (#1594, ADR-0015): the bundled Caddyfile
# carries no log routes at all — no store upstream, no forward_auth session gate — and
# nothing derived from the leftover VL_UI_* values of an older .env may reach the
# adapted configuration either.
adapt_ok no-vl-routes -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed \
    -e VL_UI_USER=hsm-logs -e VL_UI_PASSWORD=leftover-vl-password-not-for-network-use
if grep -F '"dial":"victorialogs:9428"' "$tmp/no-vl-routes.json" >/dev/null; then
    echo 'VictoriaLogs upstream survived in the adapted configuration' >&2
    exit 1
fi
if grep -F '/logs-auth' "$tmp/no-vl-routes.json" >/dev/null; then
    echo 'the /logs-auth forward_auth gate survived in the adapted configuration' >&2
    exit 1
fi
if grep -F '/logs/select' "$tmp/no-vl-routes.json" >/dev/null; then
    echo 'VictoriaLogs read routes survived in the adapted configuration' >&2
    exit 1
fi
# Every proxy that remains is the app upstream and must carry the full hsm_upstream
# treatment: the TLS skip (the app serves its self-signed certificate) and the Host
# passthrough (HSM builds absolute redirects from the request Host; without it a login
# redirect points browsers at the docker-internal name). Two proxies carry it: the
# HSM_DOMAIN site and the catch-all site, both in the one :443 server.
transport_hits="$(grep -oF '"insecure_skip_verify":true' "$tmp/no-vl-routes.json" | wc -l | tr -d ' ')"
[ "$transport_hits" -eq 2 ] || {
    echo "expected 2 proxies with insecure_skip_verify, got $transport_hits" >&2
    exit 1
}
host_hits="$(grep -oF '"Host":["{http.request.hostport}"]' "$tmp/no-vl-routes.json" | wc -l | tr -d ' ')"
[ "$host_hits" -eq 2 ] || {
    echo "expected 2 Host header_up passthroughs, got $host_hits" >&2
    exit 1
}

# Large file archives (#1885, ADR-0022 point 15): HSM decides, Caddy gives out. Both proxies
# carry the hand-over: the marker header is SET (replacing a client's value, not appended),
# and an answer with X-Hsm-Archive is consumed by a handle_response route that serves the
# named file from the read-only archives folder. file_server appears only inside those
# routes: no route of its own reaches the folder.
count_in() {
    grep -oF "$2" "$1" | wc -l | tr -d ' '
}
expect_count() {
    file="$1"
    expected="$2"
    needle="$3"
    got="$(count_in "$file" "$needle")"
    [ "$got" -eq "$expected" ] || {
        echo "expected $expected x $needle in $(basename "$file"), got $got" >&2
        exit 1
    }
}
archives_json="$tmp/self-signed.json"
expect_count "$archives_json" 2 '"set":{"Host":["{http.request.hostport}"],"X-Hsm-Serve-Archives":["1"]}'
expect_count "$archives_json" 2 '"handle_response":[{"match":{"headers":{"X-Hsm-Archive":["*"]}}'
expect_count "$archives_json" 2 '"root":"/srv/hsm-archives"'
expect_count "$archives_json" 2 '"uri":"/{http.reverse_proxy.header.X-Hsm-Archive}"'
expect_count "$archives_json" 2 '"include":["Content-Disposition","Repr-Digest","X-Hsm-Password-Version","Cache-Control"]'
expect_count "$archives_json" 2 '"handle_response"'
expect_count "$archives_json" 2 '"handler":"file_server"'
expect_count "$archives_json" 2 '"root":'
# Each file_server sits in the routes of a handle_response. The adapted JSON is one line
# with sorted keys, so a proxy's "handle_response" array is the last thing before its
# "handler":"reverse_proxy" key: cut the line at every such key and drop each piece's
# handle_response tail; no file_server may remain in what is left.
if awk '{ gsub(/,"handler":"reverse_proxy"/, "\n"); print }' "$archives_json" \
    | sed 's/"handle_response":.*$//' | grep -F 'file_server' >/dev/null; then
    echo 'a file_server is reachable outside the archive handle_response' >&2
    exit 1
fi
if grep -F '"logs"' "$archives_json" >/dev/null; then
    echo 'the archive hand-over enabled Caddy access logs' >&2
    exit 1
fi

# One port (#1590, aliases removed in #1696): the only HTTPS listener is :443 and every
# site forwards to the app's one listener on 443. Neither a listener nor an upstream of
# the old two-port scheme (44330 Sensor API, 44333 Web UI) may come back: a listener
# would present a retired address as working, a per-port upstream would 502.
grep -F '"listen":[":443"]' "$tmp/no-vl-routes.json" >/dev/null || {
    echo 'the :443 listener is missing from the adapted configuration' >&2
    exit 1
}
for retired_port in 44330 44333; do
    if grep -F ":$retired_port" "$tmp/no-vl-routes.json" >/dev/null; then
        echo "retired port $retired_port is still referenced by the adapted configuration" >&2
        exit 1
    fi
done

# Compatibility (#1580 review, kept through the #1594 removal): a customised Caddyfile
# copied from a pre-2.11.4-4 image still contains `import {$HSM_VL_SNIPPET}` (the docs of
# those releases told operators to keep it). With the variable no longer exported, the
# placeholder would expand to a bare `import` and Caddy would refuse to load — taking
# down every listener it fronts, sensor ingestion included. Adapting the frozen 2.11.4-3
# bundled Caddyfile through this image's entrypoint must therefore succeed, keep proxying
# the app upstreams, and activate neither the VictoriaLogs routes nor the basic-auth
# gate: the entrypoint pins the variable to the empty snippet those copies define — fail
# closed.
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
validate_dns_provider provision-cloudflare-override \
    -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=cloudflare -e CF_API_TOKEN=cfut_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
    -e HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=_acme-challenge.hsm.alias.example.dev
validate_dns_provider provision-dynv6-override \
    -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns \
    -e HSM_DNS_PROVIDER=dynv6 -e DYNV6_API_TOKEN=offline-test-token \
    -e HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=_acme-challenge.hsm.alias.example.dev

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
reject invalid-override 'HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN must be a bare DNS name' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=cloudflare -e CF_API_TOKEN=cfut_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa -e 'HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=_acme-challenge.alias.example.dev:443'
reject invalid-override-scheme 'HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN must be a bare DNS name' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=cloudflare -e CF_API_TOKEN=cfut_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa -e 'HSM_DNS_CHALLENGE_OVERRIDE_DOMAIN=https://alias.example.dev'
reject blank-cloudflare-token 'CF_API_TOKEN is required' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=letsencrypt-dns -e HSM_DNS_PROVIDER=cloudflare -e 'CF_API_TOKEN=   '
reject missing-custom-files 'custom mode requires readable /certs/cert.pem' -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=custom
reject invalid-domain 'HSM_DOMAIN must be a bare DNS name or IP address' -e 'HSM_DOMAIN=https://hsm.example.com' -e HSM_CERTIFICATE=self-signed
reject domain-with-port 'HSM_DOMAIN must be a bare DNS name or IP address' -e 'HSM_DOMAIN=hsm.example.com:443' -e HSM_CERTIFICATE=self-signed


# Large file archives at runtime (#1885, ADR-0022 point 15). A fake HSM upstream answers
# like HSM: an instruction (503 + X-Hsm-Archive naming <productId:N>/<archiveId:N>.zip)
# for a checked download, its own 404 for a refused one, an ordinary page otherwise, and
# an echo of the marker header it received. The image under test fronts it with the
# archives in a read-only mount; a curl container is the client.
archive_product='0b0c7c5e6a0f4d8a9f7e3c2b1a000001'
archive_id='5d41402abc4b2a76b9719d911017c592'
archive_missing_id='5d41402abc4b2a76b9719d911017dead'
archive_name="$archive_product/$archive_id.zip"
archive_digest='sha-256=:dGhlIGZha2UgdXBzdHJlYW0gc2VuZHMgdGhpcyB2ZXJiYXRpbQ==:'
curl_image='curlimages/curl:8.23.0'
mkdir -p "$tmp/archives/$archive_product" "$tmp/archive-run"
archive_file="$tmp/archives/$archive_name"
dd if=/dev/urandom of="$archive_file" bs=1048576 count=8 2>/dev/null
chmod -R a+rX "$tmp/archives"
archive_sha="$(sha256sum "$archive_file" | cut -d' ' -f1)"
archive_size="$(wc -c <"$archive_file" | tr -d ' ')"
range_sha="$(dd if="$archive_file" bs=1 skip=100 count=100 2>/dev/null | sha256sum | cut -d' ' -f1)"

cat >"$tmp/upstream.Caddyfile" <<EOF
{
	persist_config off
	local_certs
	skip_install_trust
}
https://app:443, https://hsm.example.com:443, https://proxy:443 {
	tls internal
	handle /ok {
		header X-Hsm-Archive "$archive_name"
		header Content-Disposition "attachment; filename=\"dump.dmp.zip\""
		header Repr-Digest "$archive_digest"
		header X-Hsm-Password-Version "3"
		header Cache-Control "private, no-store"
		respond "This answer is an instruction for the bundled Caddy." 503
	}
	handle /missing {
		header X-Hsm-Archive "$archive_product/$archive_missing_id.zip"
		respond "This answer is an instruction for the bundled Caddy." 503
	}
	handle /deny {
		respond "not found by hsm" 404
	}
	handle /noheader {
		respond "plain hsm page" 200
	}
	handle /marker {
		respond "marker={header.X-Hsm-Serve-Archives}" 200
	}
	handle {
		respond "app page {path}" 200
	}
}
EOF

docker network create "$archive_network" >/dev/null
docker run -d --name "$archive_upstream" --network "$archive_network" --network-alias app \
    -v "$tmp/upstream.Caddyfile:/etc/caddy/Caddyfile:ro" caddy:2.11.4 >/dev/null
docker run -d --name "$archive_proxy" --network "$archive_network" \
    --network-alias hsm.example.com --network-alias proxy \
    -e HSM_DOMAIN=hsm.example.com -e HSM_CERTIFICATE=self-signed \
    -v "$tmp/archives:/srv/hsm-archives:ro" "$image" >/dev/null
docker run -d --name "$archive_client" --network "$archive_network" \
    --entrypoint sleep "$curl_image" 600 >/dev/null

hsm_url='https://hsm.example.com'
ready=false
attempt=1
while [ "$attempt" -le 30 ]; do
    code="$(docker exec "$archive_client" curl -sk --max-time 5 -o /dev/null -w '%{http_code}' \
        "$hsm_url/noheader" 2>/dev/null || true)"
    if [ "$code" = 200 ]; then
        ready=true
        break
    fi
    sleep 1
    attempt=$((attempt + 1))
done
if [ "$ready" != true ]; then
    echo 'the archive hand-over stack did not start' >&2
    docker logs "$archive_proxy" >&2
    docker logs "$archive_upstream" >&2
    exit 1
fi

hand_over_fail() {
    echo "archive hand-over: $*" >&2
    exit 1
}
# fetch NAME CURL-ARGS...: headers to $tmp/archive-run/NAME.h, the body stays in the client.
fetch() {
    name="$1"
    shift
    docker exec "$archive_client" curl -sk --max-time 60 -D "/tmp/$name.h" -o "/tmp/$name.b" "$@" \
        || hand_over_fail "$name: curl failed"
    docker exec "$archive_client" cat "/tmp/$name.h" | tr -d '\r' >"$tmp/archive-run/$name.h"
}
status_of() {
    sed -n '1s/^HTTP\/[0-9.]* \([0-9]*\).*$/\1/p' "$tmp/archive-run/$1.h"
}
header_of() {
    grep -i "^$2:" "$tmp/archive-run/$1.h" | head -n 1 | sed 's/^[^:]*: *//'
}
body_sha() {
    docker exec "$archive_client" sha256sum "/tmp/$1.b" | cut -d' ' -f1
}
body_size() {
    docker exec "$archive_client" sh -c "wc -c < /tmp/$1.b" | tr -d ' '
}
body_text() {
    docker exec "$archive_client" cat "/tmp/$1.b"
}
expect_status() {
    got="$(status_of "$1")"
    [ "$got" = "$2" ] || hand_over_fail "$1: status $got, expected $2"
}
expect_header() {
    got="$(header_of "$1" "$2")"
    [ "$got" = "$3" ] || hand_over_fail "$1: $2 is '$got', expected '$3'"
}
expect_body() {
    got="$(body_text "$1")"
    [ "$got" = "$2" ] || hand_over_fail "$1: body is '$got', expected '$2'"
}
expect_file() {
    got="$(body_sha "$1")"
    [ "$got" = "$archive_sha" ] || hand_over_fail "$1: body SHA-256 $got, expected the archive's $archive_sha"
}
expect_not_file() {
    size="$(body_size "$1")"
    [ "$size" -lt "$archive_size" ] || hand_over_fail "$1: the client received $size bytes, the archive's size"
}
no_instruction() {
    if grep -i '^X-Hsm-Archive:' "$tmp/archive-run/$1.h" >/dev/null; then
        hand_over_fail "$1: the X-Hsm-Archive instruction reached the client"
    fi
}

# A checked download: the file itself, with its validators and HSM's chosen headers, and
# without the instruction header.
fetch full "$hsm_url/ok"
expect_status full 200
expect_file full
expect_header full Content-Length "$archive_size"
expect_header full Accept-Ranges bytes
expect_header full Content-Disposition 'attachment; filename="dump.dmp.zip"'
expect_header full Repr-Digest "$archive_digest"
expect_header full X-Hsm-Password-Version 3
expect_header full Cache-Control 'private, no-store'
no_instruction full
archive_etag="$(header_of full ETag)"
[ -n "$archive_etag" ] || hand_over_fail 'full: no ETag'

# The catch-all site (clients by IP or another name) carries the same hand-over.
fetch catch-all https://proxy/ok
expect_status catch-all 200
expect_file catch-all
no_instruction catch-all

fetch range -H 'Range: bytes=100-199' "$hsm_url/ok"
expect_status range 206
expect_header range Content-Range "bytes 100-199/$archive_size"
[ "$(body_sha range)" = "$range_sha" ] || hand_over_fail 'range: wrong bytes for 100-199'
no_instruction range

# A cut download continued where it stopped, guarded by the ETag.
docker exec "$archive_client" curl -sk --limit-rate 1M --max-time 2 -o /tmp/resume.b "$hsm_url/ok" \
    >/dev/null 2>&1 || true
cut_size="$(body_size resume)"
[ "$cut_size" -gt 0 ] && [ "$cut_size" -lt "$archive_size" ] \
    || hand_over_fail "resume: the cut download holds $cut_size of $archive_size bytes"
fetch resume -C - -H "If-Range: $archive_etag" "$hsm_url/ok"
expect_status resume 206
expect_file resume

# A stale validator gets the whole file again, not a range of a different one.
fetch stale -H 'Range: bytes=100-199' -H 'If-Range: "stale"' "$hsm_url/ok"
expect_status stale 200
expect_file stale

fetch head -I "$hsm_url/ok"
expect_status head 200
expect_header head Content-Length "$archive_size"
expect_header head ETag "$archive_etag"
no_instruction head

# HSM's refusal reaches the client as HSM gave it.
fetch deny "$hsm_url/deny"
expect_status deny 404
expect_body deny 'not found by hsm'

# Forgery: an X-Hsm-Archive the CLIENT sends is never an instruction.
fetch forged-deny -H "X-Hsm-Archive: $archive_name" "$hsm_url/deny"
expect_status forged-deny 404
expect_body forged-deny 'not found by hsm'
fetch forged-page -H "X-Hsm-Archive: $archive_name" "$hsm_url/noheader"
expect_status forged-page 200
expect_body forged-page 'plain hsm page'

# Forgery: the marker is Caddy's, whatever the client sends; on both sites.
fetch marker "$hsm_url/marker"
expect_body marker 'marker=1'
fetch forged-marker -H 'X-Hsm-Serve-Archives: 0' -H 'X-Hsm-Serve-Archives: 0' "$hsm_url/marker"
expect_body forged-marker 'marker=1'
fetch forged-marker-catch-all -H 'X-Hsm-Serve-Archives: 0' https://proxy/marker
expect_body forged-marker-catch-all 'marker=1'

# No route of its own reaches the folder: these paths go to HSM.
fetch by-name "$hsm_url/$archive_name"
expect_status by-name 200
expect_body by-name "app page /$archive_name"
fetch by-folder "$hsm_url/srv/hsm-archives/$archive_name"
expect_status by-folder 200
expect_body by-folder "app page /srv/hsm-archives/$archive_name"

# An instruction naming a file that is not there: 404 and no bytes.
fetch missing "$hsm_url/missing"
expect_status missing 404
[ "$(body_size missing)" -eq 0 ] || hand_over_fail "missing: the client received $(body_size missing) bytes"
no_instruction missing
expect_not_file deny
expect_not_file forged-deny
expect_not_file forged-page

echo 'hsm-caddy archive hand-over tests passed'

echo 'hsm-caddy runtime configuration tests passed'
