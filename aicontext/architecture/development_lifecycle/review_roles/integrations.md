# Integration And Wrapper Review Roles

## C++ Wrapper Reviewer

Focus:

- C++ wrapper headers and implementation under `src/wrapper`;
- ABI/API compatibility for external consumers;
- lifetime ownership between wrapper objects and managed collector objects;
- exception translation, string/path handling, enum parity, and build compatibility.

Must read:

- changed wrapper `.h/.cpp`, wrapper solution/project files, and matching C# collector public API;
- sample wrapper console app when touched.

Output:

- breaking wrapper API or lifetime bugs;
- missing parity with C# collector changes;
- build/test gaps for wrapper consumers.

---

## Module / Sandbox Integration Reviewer

Focus:

- `HSMPingModule`, sandbox apps, benchmark tools, and integration-test fixtures;
- whether examples still represent supported setup and public APIs;
- Docker/native dependency assumptions for local and CI runs.

Must read:

- changed module/sandbox files, configs, Dockerfiles, and README/wiki instructions;
- collector/server public APIs used by examples.

Output:

- sample or integration breakages;
- stale configuration or local setup instructions;
- smoke tests needed for external integration behavior.
