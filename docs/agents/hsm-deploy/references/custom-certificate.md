# Serving the corporate certificate through Caddy (HSM_CERTIFICATE=custom)

When every client of an HSM install is a corporate machine that already trusts the
company CA (Soft-FX Networks PKI), the `custom` certificate mode is the right end
state: Caddy serves the corporate certificate on every client-facing port, which is
exactly what the pre-compose install did when HSM itself read the PFX from
`Config/`. After a compose migration switches TLS to `self-signed`, corporate
clients that verified the old certificate stop connecting until this mode is
applied.

Shared by the `hsm-deploy-dev` and `hsm-deploy-live` skills. Applied on the dev
machine 2026-10-19. The live machine follows the same steps at (or right after) its
own `migrate-to-compose.md` run — its pre-compose `Config/*.pfx` survives the
migration untouched.

## When to choose this mode

- Clients trust the internal root CA already — they worked against the pre-compose
  install serving the same PFX, so their trust stores are proven.
- A public certificate is impossible or pointless here: the public 80/443 of these
  machines terminate on the Soft-FX gateway (htz-gw), so ACME HTTP-01/TLS-ALPN
  challenges never reach Caddy — `letsencrypt-http` cannot work behind it.
  `letsencrypt-dns` (the zone is on Cloudflare) stays available as a future option
  if non-corporate clients ever appear, at the cost of a token in `.env`.

Out of scope: the certificate on public port 443 belongs to the gateway itself
(`htz-gw.soft-fx.net`) and is wrong for HSM names by design. Clients connect on
44330/44333, which are forwarded straight to Caddy — those are the ports this mode
fixes.

## Preconditions

- The PFX: the pre-compose install leaves it in `<dir>/Config/` (dev:
  `hsm.dev.soft-fx.eu.pfx`); NOC issues fresh ones on request. Its password is the
  target machine's `Config/appsettings.json` → `ServerCertificate:Key` — read it on
  the machine, never into the chat.
- The compose file carries the `./CaddyCertificates:/certs:ro` mount (all versions
  since the compose stack exists) and `CaddyCertificates/` exists next to it.
- `CaddyCertificates/` is root-owned with no group write. If the SSH user has no
  passwordless sudo but is in the `docker` group, place the files through a
  throwaway container (step 3).

## Steps (on the target machine)

1. Convert PFX → PEM in a private temp dir. `cert.pem` is the leaf plus the
   intermediate; the self-signed root is excluded — clients must have it in their
   own trust stores. The key is written unencrypted, so keep the temp dir at 700
   and remove it in step 3. The PFX password travels through the environment
   (`export PW` + `-passin env:PW`), never on the command line — argv is visible
   in `/proc/<pid>/cmdline`, the same rule `caddy/docker-entrypoint.sh` follows
   for the VictoriaLogs password.

   ```bash
   export PW=$(sed -n 's/.*"Key"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' <dir>/Config/appsettings.json | head -1)
   TMP=$(mktemp -d /tmp/hsmcert.XXXXXX) && chmod 700 "$TMP"
   P=<dir>/Config/<machine-domain>.pfx
   openssl pkcs12 -in "$P" -passin env:PW -nokeys -clcerts -out "$TMP/leaf.pem" 2>/dev/null
   openssl pkcs12 -in "$P" -passin env:PW -nocerts -nodes  -out "$TMP/key.pem"  2>/dev/null
   openssl pkcs12 -in "$P" -passin env:PW -nokeys -cacerts -out "$TMP/cas.pem"  2>/dev/null
   unset PW
   cd "$TMP"
   awk 'BEGIN{n=0} /-----BEGIN CERTIFICATE-----/{n++} {print > ("ca" n ".pem")}' cas.pem
   cp leaf.pem cert.pem
   for f in ca*.pem; do
     [ -s "$f" ] || continue
     s=$(openssl x509 -in "$f" -noout -subject -nameopt RFC2253 2>/dev/null | sed 's/^subject=//')
     i=$(openssl x509 -in "$f" -noout -issuer  -nameopt RFC2253 2>/dev/null | sed 's/^issuer=//')
     if [ -n "$s" ] && [ "$s" = "$i" ]; then cp "$f" root.pem; else cat "$f" >> cert.pem; fi
   done
   ```

   A PFX may repeat the root (dev's did, twice) — harmless, every copy is skipped.

2. Sanity-check before placing anything: the key must match the leaf, and the chain
   must verify through the excluded root. On `EXTRACT_FAILED or KEY_MISMATCH`
   stop here — nothing has been placed yet. The emptiness check matters: a failed
   extraction (wrong password, unreadable PFX) produces empty files whose pubkeys
   hash as equal, so the match check alone would pass.

   ```bash
   if [ -s leaf.pem ] && [ -s key.pem ] \
      && [ "$(openssl pkey -in key.pem -pubout 2>/dev/null | sha256sum | cut -d' ' -f1)" \
           = "$(openssl x509 -in leaf.pem -noout -pubkey 2>/dev/null | sha256sum | cut -d' ' -f1)" ]; then
     awk '/BEGIN/{n++} n>=2' cert.pem > interm.pem
     openssl verify -CAfile root.pem -untrusted interm.pem leaf.pem   # expect: OK
   else
     echo "EXTRACT_FAILED or KEY_MISMATCH — stop here"
   fi
   ```

3. Place the files into `CaddyCertificates/` and remove the temp dir — its
   `key.pem` sits there unencrypted. Step 1 left the shell inside `$TMP`; leave it
   before deleting. With passwordless sudo:

   ```bash
   cd /tmp
   sudo install -m 644 "$TMP/cert.pem" <dir>/CaddyCertificates/cert.pem
   sudo install -m 600 "$TMP/key.pem"  <dir>/CaddyCertificates/key.pem
   rm -rf "$TMP"
   ```

   Without it (docker group only) — the image's ENTRYPOINT demands HSM_DOMAIN, so
   override it; use the same caddy image tag the stack runs:

   ```bash
   cd /tmp
   docker run --entrypoint sh --rm -v "$TMP":/src:ro -v <dir>/CaddyCertificates:/dst \
     hsmonitoring/hsm-caddy:2.11.4-3 -c \
     'cp /src/cert.pem /dst/cert.pem && cp /src/key.pem /dst/key.pem && chmod 644 /dst/cert.pem && chmod 600 /dst/key.pem'
   rm -rf "$TMP"
   ```

4. The user switches the mode in `<dir>/.env` — the agent never edits `.env`:

   ```
   HSM_CERTIFICATE=custom
   ```

   The caddy entrypoint refuses to start in this mode without readable
   `/certs/cert.pem` and `/certs/key.pem` (`caddy/docker-entrypoint.sh`).

5. Apply with the machine's full `-f` file set (see the top of troubleshooting.md)
   — only caddy is recreated, the app container and data are untouched. Then
   verify from outside the machine:

   ```bash
   echo | openssl s_client -connect <domain>:44333 -servername <domain> 2>/dev/null \
     | openssl x509 -noout -subject -issuer -dates
   # expect the corporate issuer, CN/SAN = <domain>, expiry of the PFX; served
   # chain: leaf plus every intermediate (2 certs for a single-intermediate PFX)
   curl -o /dev/null -w "%{http_code}\n" https://<domain>:44333/api/sensors/testConnection   # 200
   ```

   From a corporate workstation the curl needs no `-k`: the Soft-FX root is already
   trusted — which is the whole point of the mode.

## Rollback

`HSM_CERTIFICATE=self-signed` in `.env` plus `up -d` (full `-f` set). The mode is
the only thing that changed; cert/key files may stay in place — other modes ignore
them.

## Renewal (the machine owner's responsibility)

`custom` has no auto-renewal. The dev certificate expires **2027-05-21**. A month
before expiry: obtain a fresh PFX from NOC into `Config/`, repeat steps 1–3 (they
overwrite cert.pem/key.pem), then `docker restart hsm-caddy` — a plain container
restart, not a compose command, so it needs none of the machine's compose `-f`
files (troubleshooting.md) — and a restart is enough once the mode is already
`custom`: the files are re-read at container start and no `.env` change is needed.
A calendar reminder (and/or an HSM sensor on the
cert expiry) belongs on whoever runs the machine.

## Notes

- Never commit cert.pem/key.pem or the PFX; the PFX password stays on the machine
  it came from.
- A custom Caddyfile's extra vhosts keep their own TLS policy: the dev mTLS vhost
  for VictoriaLogs (`logs.<domain>`, internal CA + client certificates) is
  untouched by this mode.
