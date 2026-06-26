string(TIMESTAMP TS "%Y-%m-%d %H:%M:%S UTC")
file(WRITE "${OUTPUT}"
    "// Auto-generated at build time — do not edit.\n"
    "namespace hsm { namespace agent {\n"
    "    const char* BuildTimestamp() { return \"${TS}\"; }\n"
    "} }\n"
)
