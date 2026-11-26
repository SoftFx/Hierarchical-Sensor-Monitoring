# The table sensors' end-to-end driver (hsm_table_e2e.cpp) built on the Linux agent's stack: Debian 13
# with the distribution's libcurl (OpenSSL, WebSocket), zstd, zlib and nlohmann-json, no vcpkg.
# The build context is src/native/collector:
#   docker build -f src/native/collector/tests/server_e2e/Dockerfile -t hsm-native-table-e2e src/native/collector
# NativeTableSensorEndToEndTests (src/collector/HSMDataCollector.IntegrationTests) builds and runs it.
FROM debian:13

RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential cmake pkg-config \
       libcurl4-openssl-dev libzstd-dev zlib1g-dev nlohmann-json3-dev \
    && rm -rf /var/lib/apt/lists/*

COPY . /src

# No -DHSM_COLLECTOR_WERROR: the warning gates are the native lanes' (native-collector-conformance.yml); a new
# warning of this distribution's compiler must not fail the run of the rows against the server.
RUN cmake -S /src -B /build -DCMAKE_BUILD_TYPE=Release \
       -DHSM_COLLECTOR_HTTP=ON -DHSM_COLLECTOR_TABLES=ON -DHSM_COLLECTOR_BUILD_EXAMPLES=OFF \
    && cmake --build /build --target hsm_table_e2e --parallel \
    && cp /build/hsm_table_e2e /usr/local/bin/ \
    && rm -rf /build

ENTRYPOINT ["hsm_table_e2e"]
