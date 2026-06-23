# Agent binary drop-point

The per-product download endpoint (`GET /api/agent/installer?productId=…`, epic #1167 W6/W7) serves a
zip containing the prebuilt **`hsm-agent.exe`** from this directory, plus a generated `config.json` and
install scripts.

Publish the signed release `hsm-agent.exe` here as `wwwroot/agent/hsm-agent.exe`. CI packaging wires this
up (W8). Until the binary is present the endpoint returns HTTP 503 with a clear message — the rest of the
download flow (config + scripts) is exercised by unit tests.

The exe is served **byte-identical** to the published, signed binary; only `config.json` is generated
per product (Authenticode invariant).
