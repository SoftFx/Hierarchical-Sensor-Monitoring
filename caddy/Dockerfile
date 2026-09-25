ARG CADDY_VERSION=2.11.4

FROM caddy:${CADDY_VERSION}-builder-alpine AS builder

ARG CADDY_VERSION=2.11.4
ARG CLOUDFLARE_MODULE=v0.2.4
ARG DYNV6_MODULE=71fad600afb29911aa04cbfd99f7d5d878c3bc48

RUN xcaddy build "v${CADDY_VERSION}" \
    --with "github.com/caddy-dns/cloudflare@${CLOUDFLARE_MODULE}" \
    --with "github.com/caddy-dns/dynv6@${DYNV6_MODULE}"

FROM caddy:${CADDY_VERSION}-alpine

COPY --from=builder /usr/bin/caddy /usr/bin/caddy
COPY Caddyfile /etc/caddy/Caddyfile
COPY docker-entrypoint.sh /usr/bin/hsm-caddy-entrypoint

RUN chmod 0555 /usr/bin/hsm-caddy-entrypoint \
    && caddy list-modules | grep -Fx 'dns.providers.cloudflare' \
    && caddy list-modules | grep -Fx 'dns.providers.dynv6'

ENTRYPOINT ["/usr/bin/hsm-caddy-entrypoint"]
CMD ["caddy", "run", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"]
