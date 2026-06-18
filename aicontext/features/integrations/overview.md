# Integration Features Overview

> Owner: integrations | Last reviewed: 2026-05-28 | Canonical: yes

The integrations area owns external integration surfaces and examples: C++
wrapper, ping module, sandbox apps, benchmark tools, Docker-facing examples, and
documentation that shows third parties how to use HSM.

## Feature Folders

- `native-collector/` - public C++ RAII API over the native collector C ABI: lifetime model,
  `find_package` packaging, console example, and the C++/CLI-wrapper migration story (#1100).

## Feature Folders To Add Here

- `cpp-wrapper/` - legacy C++/CLI wrapper (`src/wrapper/`); superseded by `native-collector/`.
- `ping-module/` - module behavior, deployment, collector/server interaction.
- `sandbox-apps/` - sample apps and local smoke-test utilities.
- `benchmarks/` - benchmark and performance test harnesses.

Create folders from `../_TEMPLATE_feature.md` as work lands.
