# Installation

HSM Server is distributed as a Docker image. This page covers all deployment methods.

---

## Prerequisites

- Docker with Docker Compose v2.23.1 or newer
- Ports 44330 and 44333 available on the host. Caddy also publishes 80 and 443 for HTTP-01 validation and the standard web UI; DNS-01, self-signed, and custom certificate modes do not need port 80 for certificate issuance.

---

## Method 1 — Docker Compose (recommended)

The supported compose file runs HSM behind the ready-made hsmonitoring/hsm-caddy:2.11.4-1 image. You do not build Caddy or its DNS modules locally. Caddy terminates TLS and forwards requests to HSM, which remains reachable only inside the compose network.

Pull requests build and test without publishing. The trusted-master CI workflow publishes only after its checks pass. Versioned image tags are immutable: a Caddy source, configuration, or module update requires a new workflow version and matching compose image tag. The workflow refuses to overwrite an existing version; latest moves only after a new version publishes. After merge, wait for the successful master workflow before deploying a newly introduced tag.

Repository maintainers can use scripts/local-docker-build.ps1 to build the Caddy image locally as hsm-caddy:local for development; its generated Compose override selects that image. Normal installations use the published versioned image and do not build Caddy.

Choose one certificate mode in .env. There is no automatic fallback:

| Mode | Required settings | Behavior |
|---|---|---|
| letsencrypt-http | HSM_DOMAIN | Let's Encrypt HTTP-01/TLS-ALPN; public DNS must point to this host and ports 80/443 must be reachable. |
| letsencrypt | HSM_DOMAIN | Backward-compatible alias for letsencrypt-http. |
| letsencrypt-dns | HSM_DOMAIN, HSM_DNS_PROVIDER, matching provider token | Let's Encrypt DNS-01; inbound port 80 is not required. |
| self-signed | HSM_DOMAIN | Caddy's internal certificate; clients must trust its CA or allow untrusted TLS. |
| custom | HSM_DOMAIN, PEM files | Uses the provided certificate chain and private key from read-only mounted files. |
| Without Caddy | docker-compose.direct.yml | HSM serves its existing PFX certificate directly. |

**1. Download the reference compose file** (the supported file is reproduced in [Reference docker-compose.yml](#reference-docker-composeyml)):

~~~bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.yml
~~~

**2. Create the settings file.** Download the commented template next to docker-compose.yml and save it as .env:

~~~bash
curl -o .env https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/.env.example
~~~

Before the first docker compose up, edit .env: set HSM_DOMAIN to your real host name and, because the template defaults to Cloudflare DNS-01, provide CF_API_TOKEN. The copied template cannot start with its blank token. If inbound HTTP/TLS validation is available and you prefer it, change HSM_CERTIFICATE to letsencrypt-http instead.

For Cloudflare DNS validation (the documented DNS-01 example), use a scoped API token with Zone:DNS:Edit and Zone:Zone:Read for the selected zone:

~~~dotenv
HSM_DOMAIN=hsm.example.com
HSM_CERTIFICATE=letsencrypt-dns
HSM_DNS_PROVIDER=cloudflare
CF_API_TOKEN=your-token
~~~

For dynv6, set HSM_DNS_PROVIDER=dynv6 and provide DYNV6_API_TOKEN. Only the selected provider token is used. Keep .env private: docker compose config expands and prints its values, so do not share that output.

If inbound ports are reachable and you do not need DNS-01, use HTTP validation:

~~~dotenv
HSM_DOMAIN=hsm.example.com
HSM_CERTIFICATE=letsencrypt-http
~~~

| Setting | Value |
|---|---|
| HSM_DOMAIN | Bare host name or IP clients use. No https://, port, or path. |
| HSM_CERTIFICATE | letsencrypt-http (or legacy letsencrypt), letsencrypt-dns, self-signed, or custom. |
| HSM_DNS_PROVIDER | cloudflare or dynv6; required only for letsencrypt-dns. |
| CF_API_TOKEN | Cloudflare token with DNS edit and zone read access; required only for Cloudflare. |
| DYNV6_API_TOKEN | dynv6 token; required only for dynv6. |

Keep HSM_DOMAIN and HSM_CERTIFICATE in .env for Compose commands. DNS tokens are needed only for DNS-01.

For custom certificates, put the full chain in CaddyCertificates/cert.pem and the matching key in CaddyCertificates/key.pem. Compose mounts this directory read-only at /certs. Renew the files externally, then run docker compose restart caddy; Caddy does not renew custom certificates.

For custom mode, also set HSM_CERTIFICATE=custom in .env:

~~~dotenv
HSM_CERTIFICATE=custom
~~~

**3. Start the server:**

~~~bash
docker compose up -d
~~~

**4. Open the web UI:** https://<HSM_DOMAIN> or https://<HSM_DOMAIN>:44333.

Default credentials: login default, password default. **Change the password immediately.**

Collectors and agents connect to https://<HSM_DOMAIN>:44330, as before.

**5. Check the certificate.** Run on any machine:

~~~bash
openssl s_client -connect hsm.example.com:443 -servername hsm.example.com </dev/null 2>/dev/null | openssl x509 -noout -issuer -enddate
~~~

The issuer identifies the certificate authority; notAfter shows expiry. If there is no output, inspect docker logs hsm-caddy.

### How the certificate is obtained and renewed

**letsencrypt-http (or letsencrypt alias):** Caddy uses HTTP-01 and/or TLS-ALPN validation. Public DNS must resolve to this host, and the relevant public ports must reach Caddy. Certificates and ACME state are kept in CaddyData. Caddy renews automatically and continues serving a still-valid certificate while retrying a failed renewal. If initial issuance fails, HTTPS has no certificate; inspect docker logs hsm-caddy. There is no automatic mode change.

**letsencrypt-dns:** Caddy creates the required DNS TXT challenge using the selected provider token. Incoming HTTP challenge traffic is not required, so a trusted certificate can be issued when the server is behind CGNAT. DNS-01 proves control of the name for issuance only; it does not make HSM reachable from the internet. A/AAAA records, LAN routing, firewall rules, and the external access path remain separate network configuration.

**self-signed:** Caddy issues and renews its internal certificate. Browsers warn unless the Caddy local CA is installed; collectors and agents need their untrusted-certificate option if the CA is not trusted. The CA certificate is stored beside the compose file at CaddyData/caddy/pki/authorities/local/root.crt.

**custom:** Caddy serves the supplied PEM full chain and key. Renew externally and restart Caddy after replacing the files.

### Switching the certificate mode

Edit HSM_CERTIFICATE and any mode-specific settings in .env, then run:

~~~bash
docker compose up -d
~~~

Caddy is recreated and HSM data is untouched. There is no automatic fallback if issuance fails. Fix DNS or inbound reachability for HTTP-01, choose DNS-01 with a provider token, or deliberately select another mode. For custom certificates, install renewed files and restart Caddy. After trusted Let's Encrypt service, browsers may pin HSM_DOMAIN and its subdomains with HSTS for up to 30 days. If you switch to self-signed during that period, those browsers may refuse the web UI without a bypass; use a dedicated HSM hostname and plan certificate-mode changes accordingly.

### Advanced Caddyfile customization

The image includes the supported Caddyfile. The old inline Caddyfile from earlier compose setups is no longer part of the compose file, so copy any settings you still need into your own mounted Caddyfile. To restore customizations such as an ACME contact email, download the bundled file and edit your copy. Keep the global options and HSM routes, including the import that uses the entrypoint-selected HSM_TLS_SNIPPET. To set the ACME account contact email, add this directive inside the leading global options block:

~~~bash
curl -o Caddyfile https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/caddy/Caddyfile
~~~

~~~caddyfile
email admin@example.com
~~~

Then create docker-compose.override.yml beside docker-compose.yml:

~~~yaml
services:
  caddy:
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
~~~

Compose automatically combines this override with the reference file. After changing the Caddyfile, the safest option is docker compose restart caddy. To reload without restarting, run the entrypoint wrapper so it selects and exports the configured TLS snippet:

~~~bash
docker exec hsm-caddy hsm-caddy-entrypoint caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
~~~

A direct docker exec hsm-caddy caddy reload skips the entrypoint; HSM_TLS_SNIPPET is then unset and the Caddyfile cannot select the configured TLS mode. Keep the override file and mount in place for future Compose operations.

### Without Caddy

HSM serves HTTPS itself with its own certificate, exactly as before Caddy was added. Use this if you do not want Caddy, or while Caddy cannot start. Download the second compose file next to the first one. Both use the same data folders, so you can switch in both directions without losing anything:

```bash
curl -O https://raw.githubusercontent.com/SoftFx/Hierarchical-Sensor-Monitoring/master/docker-compose.direct.yml

docker compose down                                 # stop the Caddy stack
docker compose -f docker-compose.direct.yml up -d   # start HSM alone
```

and back:

```bash
docker compose -f docker-compose.direct.yml down
docker compose up -d
```

Collectors and agents keep using `https://<host>:44330`, with "allow untrusted certificate" unless you installed your own `.pfx` (see [Server Configuration](Server-Configuration)). Ports `80` and `443` are not used.

### Reference docker-compose.yml

This is the supported setup, the same file as [`docker-compose.yml`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker-compose.yml) in the repository:

```yaml
# HSM Server behind Caddy. Everyone runs the same stack with `docker compose up -d`.
#
# Caddy terminates TLS for clients and obtains/renews the certificate by itself. HSM keeps
# serving HTTPS with its own (self-signed by default) certificate, reachable only inside the
# compose network; Caddy connects to it without verifying that certificate.
# SETTINGS: choose a certificate mode in `.env`; compose refuses to start without HSM_DOMAIN
# and HSM_CERTIFICATE. Start from the template: `cp .env.example .env`, then edit it.
#
#   HSM_DOMAIN       the address clients use: a bare DNS name or IP address
#                    (no https://, no port). Examples: hsm.example.com, 10.0.0.5
#
#   HSM_CERTIFICATE  which certificate clients get:
#     letsencrypt    backward-compatible alias for letsencrypt-http
#     letsencrypt-http trusted certificate from Let's Encrypt using HTTP-01/TLS-ALPN. Needs a
#                    public DNS name and ports 80/443 open. There is no automatic fallback.
#     letsencrypt-dns trusted Let's Encrypt certificate using a DNS challenge. Also set
#                    HSM_DNS_PROVIDER=cloudflare and CF_API_TOKEN, or
#                    HSM_DNS_PROVIDER=dynv6 and DYNV6_API_TOKEN. Port 80 is not required.
#     self-signed    Caddy's own certificate. Browsers warn; collectors/agents need
#                    "allow untrusted certificate".
#     custom         your PEM certificate and key at CaddyCertificates/cert.pem and key.pem.
#                    Renewal and `docker compose restart caddy` are your responsibility.
#
# SWITCHING: edit .env and run `docker compose up -d` (Caddy is recreated; HSM data is untouched).
# WITHOUT CADDY (HSM's own PFX certificate, as before): use docker-compose.direct.yml instead.
# Collectors and agents always connect to https://<HSM_DOMAIN>:44330.
#
# The image is published by CI (server-build.yml). To run a build from local sources instead,
# publish it to this exact tag first:
#   dotnet publish src/server/HSMServer/HSMServer.csproj -c Release --os linux --arch x64 \
#     -p:PublishProfile=DefaultContainer -p:ContainerImageName=hsmonitoring/hierarchical_sensor_monitoring
# That publish cannot carry the image's HEALTHCHECK (the .NET SDK has no property for it), so add
# it the way CI does, or use scripts/local-docker-build.ps1, which does both steps:
#   docker build --build-arg BASE_IMAGE=hsmonitoring/hierarchical_sensor_monitoring:latest \
#     -t hsmonitoring/hierarchical_sensor_monitoring:latest \
#     -f docker_scripts/HSMserver/Dockerfile.healthcheck docker_scripts/HSMserver
services:
  app:
    image: 'hsmonitoring/hierarchical_sensor_monitoring:latest'
    container_name: hsm-server
    restart: unless-stopped
    user: '0'
    # No ports: HSM is reachable only through caddy.
    healthcheck:
      # Byte-identical to the one the image itself now carries (#1465, see
      # docker_scripts/HSMserver/Dockerfile.healthcheck); scripts/check-healthcheck-sync.py fails
      # the build if the two ever differ. It is repeated here only so this file also works with an
      # image published before #1465: `depends_on: service_healthy` below refuses to start caddy
      # ("container hsm-server has no healthcheck configured") when neither the image nor this
      # file defines one. Verified against the published 3.41.5, which has wget but no healthcheck.
      # (An image older than ~3.41 has no wget either; with one of those, pin an older compose file.)
      # Kestrel opens its ports only after the database has loaded, so a 200 means loaded AND
      # serving; a listening but wedged server fails this probe, while the TCP connect used here
      # before reported it healthy.
      test: ['CMD-SHELL', 'wget --quiet --tries=1 --timeout=4 --no-check-certificate --output-document=/dev/null https://127.0.0.1:44330/api/sensors/testConnection || exit 1']
      interval: 30s
      timeout: 5s
      # Budget before caddy is skipped for good (see depends_on below): 10 min + 3 x 30 s. A
      # database that needs longer: raise start_period here (and in the image, or they drift).
      # One known case: an install with legacy SensorValues_* folders rewrites all of them before
      # HSM listens. That migration continues in the container even after the budget runs out —
      # wait for it to finish (`docker ps` shows healthy again), then `docker compose up -d` again.
      retries: 3
      start_period: 10m
    environment:
      # Trust X-Forwarded-For only from the compose network, whose only other member is caddy.
      Kestrel__TrustedProxies__0: 'attached-networks'
    volumes:
      - ./Logs:/app/Logs
      - ./Config:/app/Config
      - ./Databases:/app/Databases
      - ./DatabasesBackups:/app/DatabasesBackups

  caddy:
    image: 'hsmonitoring/hsm-caddy:2.11.4-1'
    container_name: hsm-caddy
    restart: unless-stopped
    depends_on:
      app:
        condition: service_healthy
    environment:
      # Tokens are passed through the environment, not written into the Caddyfile, and Caddy config
      # persistence is disabled. Keep .env private and do not share docker compose config output.
      HSM_DOMAIN: '${HSM_DOMAIN:?Set HSM_DOMAIN in .env next to docker-compose.yml}'
      HSM_CERTIFICATE: '${HSM_CERTIFICATE:?Set HSM_CERTIFICATE in .env - see its comments}'
      HSM_DNS_PROVIDER: '${HSM_DNS_PROVIDER:-}'
      CF_API_TOKEN: '${CF_API_TOKEN:-}'
      DYNV6_API_TOKEN: '${DYNV6_API_TOKEN:-}'
    ports:
      - '80:80'
      - '443:443'
      - '44330:44330'
      - '44333:44333'
    volumes:
      - ./CaddyData:/data
      - ./CaddyCertificates:/certs:ro
```

What must stay as it is, if you ever adapt it:

| Part | Why |
|---|---|
| No ports on app | HSM is reachable only through Caddy in this compose setup. |
| `healthcheck` on `app` + `condition: service_healthy` | Caddy starts only when HSM is serving. The check is an HTTPS request to the Sensor API every 30 s: `docker ps` shows `starting`, then `healthy`, and `unhealthy` if the server stops answering. From this version on the HSM image carries the same check itself, so any deployment — including `docker-compose.direct.yml` and a plain `docker run` — shows HSM's health; the copy here is kept identical so this file also works with an older image. HSM gets 10 minutes to load its database, plus 3 failed probes; past that `app` is `unhealthy`, `docker compose up` reports "dependency failed to start" and Caddy is not created. A database that needs longer: raise `start_period`. A very old installation converts its history on the first start of a new version, which can take longer than that. The conversion keeps running inside the container: wait until `docker ps` shows `hsm-server` healthy again (`docker logs hsm-server` shows `Now listening`), then run `docker compose up -d` again — repeating it earlier gives the same error, and restarting the container only starts the conversion over. |
| Separate reverse proxies to app:44333 and app:44330 | Not a split of the UI from the Sensor API — both listeners serve the same routes, and the API answers on 44333 as well. It keeps each published port mapped to the same HSM port, so collectors and agents already configured for 44330 keep working unchanged. Only the management API, MCP, Swagger and browser sign-in are restricted to the site port, so never send the web UI to 44330. |
| tls_insecure_skip_verify on the upstream | HSM serves its own HTTPS certificate inside the compose network. |
| HSM_DOMAIN and HSM_CERTIFICATE in Caddy's environment | The entrypoint validates the mode and selects exactly one TLS source. No automatic fallback is used. |
| Provider tokens passed as container environment | The Caddyfile reads secrets from the process environment; they are not written into its configuration or persisted Caddy state. Compose-rendered config can reveal values, so do not share it. |
| persist_config off and no Caddy access log | Expanded configuration is not persisted, and request headers that may contain HSM access keys are not recorded. |
| ./CaddyData:/data | Keeps ACME accounts and certificates across restarts and updates. |
| ./CaddyCertificates:/certs:ro | Supplies custom PEM files without allowing the container to modify them. |
| Pinned hsmonitoring/hsm-caddy:2.11.4-1 | Provides Caddy 2.11.4 with Cloudflare v0.2.4 and dynv6 DNS modules; users do not build locally. |
| Published ports 44330 and 44333 | Collectors and downloaded agent bundles use Sensor API port 44330; the UI is also available on 44333. |

### Internal DNS name

A publicly trusted Let's Encrypt certificate needs a publicly registered DNS name. DNS-01 can validate it using a TXT record even when the HSM host is behind CGNAT or has no inbound port 80. This validates the name for issuance only: external clients still need a working route, public address, firewall/NAT path, and matching A/AAAA records to reach the service. For a private-only name that Let's Encrypt cannot validate, use self-signed or a suitable custom certificate. When clients need to trust Caddy's internal CA, install CaddyData/caddy/pki/authorities/local/root.crt on those clients.

### Moving an existing installation to Caddy

Nothing changes on the HSM side: it keeps its data, settings and own certificate.

**Before you stop the old stack**, check ports `80` and `443`:

- **They must be free on the host.** If they are taken, Caddy cannot start after the old stack is already down. In that case, run [without Caddy](#without-caddy) until the ports are free.
- **They become open to the network.** Docker publishes ports past host firewalls such as `ufw`, so a host that exposed only `44330`/`44333` now also answers on `80` and `443`. If the server must not be reachable on these ports, use `self-signed` and remove the `80:80` and `443:443` lines from the `caddy` service.

Then replace your `docker-compose.yml` with the reference one, create the `.env` file (step 2 above), and restart:

```bash
docker compose down
docker compose up -d
```

Check the certificate (step 5 above). What happens to existing clients:

- **Clients that use `HSM_DOMAIN`** (`https://hsm.example.com:44330`) get the new certificate. With `letsencrypt` it is trusted, so for them you can switch off "Allow untrusted server certificate".
- **Clients that use any other address**, such as the server's IP or a short internal name, keep working, including already distributed agent bundles. They receive the `HSM_DOMAIN` certificate, which does not match their address, so they must keep "allow untrusted certificate" on. To give them the trusted certificate, switch them to the `HSM_DOMAIN` address. For agents, set **Agent connection URL** (Configuration → Agent) to `https://<HSM_DOMAIN>:44330` and redistribute the bundles.

---

## Method 2 — docker run (manual)

**1. Pull the image:**

```bash
docker pull hsmonitoring/hierarchical_sensor_monitoring:latest
```

**2. Run the container.** Without Caddy in front, HSM serves HTTPS itself with a built-in self-signed certificate (or your own, see [Server Configuration](Server-Configuration)).

```bash
docker run -u 0 -d \
  --restart unless-stopped \
  -v /host/path/Logs:/app/Logs \
  -v /host/path/Config:/app/Config \
  -v /host/path/Databases:/app/Databases \
  -v /host/path/DatabasesBackups:/app/DatabasesBackups \
  -p 44330:44330 \
  -p 44333:44333 \
  hsmonitoring/hierarchical_sensor_monitoring:latest
```

Replace `/host/path/` with an actual directory on your machine.

**Windows example:**

```bash
docker run -u 0 -d ^
  --restart unless-stopped ^
  -v C:\HSM\Logs:/app/Logs ^
  -v C:\HSM\Config:/app/Config ^
  -v C:\HSM\Databases:/app/Databases ^
  -v C:\HSM\DatabasesBackups:/app/DatabasesBackups ^
  -p 44330:44330 ^
  -p 44333:44333 ^
  hsmonitoring/hierarchical_sensor_monitoring:latest
```

---

## Method 3 — Script

Ready-made scripts are available in the repository:

**PowerShell (Windows):**

Download and run [`docker_scripts/HSMserver/server_load.ps1`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker_scripts/HSMserver/server_load.ps1)

```powershell
.\server_load.ps1
```

**Bash (Linux/macOS):**

Download and run [`docker_scripts/HSMserver/load.sh`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docker_scripts/HSMserver/load.sh)

```bash
chmod +x load.sh && ./load.sh
```

The scripts pull the latest image and start the container with the correct volume and port mappings.

---

## Volume Mounts — Important

Always mount these directories. Without them, **all data is lost** when the container is stopped or updated:

| Host path | Container path | Contents |
|---|---|---|
| `./Logs` | `/app/Logs` | Application logs |
| `./Config` | `/app/Config` | `appsettings.json`, TLS certificates |
| `./CaddyData` | `/data` (caddy) | Caddy's certificates and Let's Encrypt account (compose only) |
| `./Databases` | `/app/Databases` | All sensor data (LevelDB) |
| `./DatabasesBackups` | `/app/DatabasesBackups` | Automatic database backups |

> The `Databases` volume is the most critical — it contains all sensor history and configuration. Never remove it without a backup.

---

## Ports

| Port | Protocol | Purpose |
|---|---|---|
| `44330` | HTTPS | Sensor data ingestion — used by DataCollector and REST API |
| `44333` | HTTPS | Web UI |
| `443` | HTTPS | Web UI on the standard port (compose only; can be removed for IP-address setups) |
| `80` | HTTP | Let's Encrypt domain check and redirect to HTTPS (compose only; can be removed for IP-address setups) |

With Docker Compose, HSM is reachable only through Caddy, and Caddy publishes these ports. Keep `44330` and `44333`: downloaded agent bundles point at port `44330`. If you must use other host ports, change the left side of the mapping in the `caddy` service and set **Agent connection URL** (Configuration → Agent) to the new Sensor API address, e.g. `https://hsm.example.com:44331`.

For `docker run`, change the mapping and `Config/appsettings.json`:

```yaml
ports:
  - '44331:44330'   # host port : container port
  - '44334:44333'
```

```json
"Kestrel": {
  "SensorPort": 44330,
  "SitePort": 44333
}
```

Note: the `Kestrel` config defines the ports the server listens on inside the container. The left side of the Docker port mapping is what you expose on the host.

---

## Updating

To update to the latest version:

```bash
# Pull the new images (HSM and Caddy)
docker compose pull

# Restart with Docker Compose (data is preserved in volumes)
docker compose down
docker compose up -d
```

Without Caddy, add `-f docker-compose.direct.yml` to each `docker compose` command. With `docker run`, stop and remove the old container, then run the `docker run` command again. Volumes are not affected.

---

## First Login

After starting the server, open `https://<HSM_DOMAIN>:44333` (compose) or `https://localhost:44333` (`docker run`).

- Username: `default`
- Password: `default`

Go to **Account → Settings** and change the password before doing anything else.

---

## Next Steps

- [Server Configuration](Server-Configuration) — ports, TLS, backups, Telegram bot
- [Getting Started](Getting-Started) — create a product and send your first sensor value
