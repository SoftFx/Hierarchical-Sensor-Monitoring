//! The privileged half of top disk I/O (#1547), and what the unprivileged probe shares with it.
//!
//! Reading another user's `/proc/<pid>/io` needs `CAP_DAC_READ_SEARCH` (the file is 0400) and
//! `CAP_SYS_PTRACE` (the kernel's ptrace-access check). Held by the probe, those two would let it
//! read the whole host filesystem through `/proc/<pid>/root`. So they are held by this helper
//! only: a root-run daemon (`hsm-linux-probe-io.service`) that reads `/proc/<pid>/stat`,
//! `/proc/<pid>/io` and `/proc/<pid>/task/<tid>/io` of every process and the `/proc/<pid>/exe` link
//! of one with a 15-byte name — nothing else, no network, no other file but its config — and
//! writes one snapshot file ([`snapshot`]) the probe reads. The probe keeps no capability.
//!
//! * [`procstat`] — `/proc/<pid>/stat` parsing, the process table and the full-name rule of a
//!   15-byte `comm` ([`procstat::extended_name`]), shared with the probe's top-CPU sampler;
//! * [`scan`] — one scan, made race-free against reaps in the middle of it, with each process's
//!   reaped part (#1572), and [`scan::receiver`], the reaper walk the probe also uses;
//! * [`snapshot`] — the file format, written atomically;
//! * [`clock`] — local time (the day's final reading) and the monotonic clock both sides compare.

pub mod clock;
pub mod procstat;
pub mod scan;
pub mod snapshot;
