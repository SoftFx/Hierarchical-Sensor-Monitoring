//! Builds the in-tree native collector (`src/native/collector`) with CMake, checks that this
//! crate's FFI mirrors still match the real C header, and links the result.
//!
//! The probe consumes the collector exactly as the agent does: a CMake build with
//! `HSM_COLLECTOR_HTTP=ON`, static `hsm_collector_core`, libcurl resolved by `find_package(CURL)`
//! and linked dynamically against the distro package (initiative §4.5 — OpenSSL security fixes
//! must arrive through `apt upgrade`, not a probe rebuild).
//!
//! TODO(#1413): once workstream 1 (#1414) is merged and published as a `collector-v*` tag, switch
//! this from the in-tree source tree to the pinned version from the repo's vcpkg registry
//! (`ports/hsm-collector`), so the probe builds against the same published artifact every other
//! native consumer uses. Until that release exists, the in-tree build is the only way to get a
//! collector that contains the Linux metric sources.

use std::env;
use std::path::{Path, PathBuf};
use std::process::Command;

/// The expected C layout of every struct this crate mirrors, in one place.
///
/// These numbers are checked against BOTH sides, so neither can drift silently:
/// * against C, by compiling `static_assert`s over the real header ([`check_abi_layout`]);
/// * against Rust, by the `layout_tests` in `src/lib.rs`, which read them back from the
///   `HSM_ABI_*` env vars this script emits.
///
/// A collector that appends a field to one of these structs therefore fails the build here — the
/// collector's versioning policy calls that a MINOR bump, so nothing else would notice.
struct Layout {
    /// C struct name, also the `HSM_ABI_<NAME>_SIZE` / `_ALIGN` env-var stem.
    name: &'static str,
    size: usize,
    align: usize,
    /// Every field with its byte offset, so an inserted or reordered field fails too.
    fields: &'static [(&'static str, usize)],
}

const LAYOUTS: &[Layout] = &[
    Layout {
        name: "hsm_collector_options_t",
        size: 88,
        align: 8,
        fields: &[
            ("access_key", 0),
            ("server_address", 8),
            ("port", 16),
            ("client_name", 24),
            ("module", 32),
            ("computer_name", 40),
            ("max_queue_size", 48),
            ("max_values_in_package", 52),
            ("package_collect_period_ms", 56),
            ("request_timeout_ms", 60),
            ("max_sensors", 64),
            ("allow_untrusted_server_certificate", 68),
            ("allow_plaintext_transport", 69),
            ("exception_deduplicator_window_ms", 72),
            ("max_deduplicated_messages", 80),
        ],
    },
    Layout {
        name: "hsm_sensor_options_t",
        size: 80,
        align: 8,
        fields: &[
            ("ttl_ms", 0),
            ("unit", 8),
            ("description", 16),
            ("keep_history_ms", 24),
            ("self_destroy_ms", 32),
            ("display_unit", 40),
            ("statistics", 44),
            ("is_singleton", 48),
            ("aggregate_data", 52),
            ("enable_grafana", 56),
            ("is_computer_sensor", 60),
            ("sensor_location", 64),
            ("default_alert_options", 72),
        ],
    },
    Layout {
        name: "hsm_enum_option_t",
        size: 32,
        align: 8,
        fields: &[("key", 0), ("value", 8), ("color", 16), ("description", 24)],
    },
    Layout {
        name: "hsm_default_sensor_params_t",
        size: 48,
        align: 8,
        fields: &[
            ("process_name", 0),
            ("disk_letter", 8),
            ("interface_name", 16),
            ("service_name", 24),
            ("is_host_service", 32),
            ("product_version", 40),
        ],
    },
];

/// The C enum types this crate mirrors as `i32` aliases. Each must stay 4 bytes (the header pins
/// them with `HSM_ENUM_INT32`), or the by-value arguments would be passed with the wrong width.
const ENUM_TYPES: &[&str] = &[
    "hsm_alert_kind_t",
    "hsm_alert_combination_t",
    "hsm_alert_operation_t",
    "hsm_alert_property_t",
    "hsm_alert_target_type_t",
    "hsm_alert_destination_mode_t",
    "hsm_alert_repeat_mode_t",
    "hsm_alert_icon_t",
];

/// Every enumerator value this crate hard-codes, checked the same two ways as [`LAYOUTS`]: against
/// C with a `static_assert` here, and against the Rust constants by a test in `src/lib.rs` that
/// reads this table back from `$OUT_DIR/abi_enum_values.rs`. They are wire values (the managed
/// AlertOperation/AlertProperty/... numbers), so a silent mismatch would register the wrong alert.
const ENUM_VALUES: &[(&str, i64)] = &[
    ("HSM_ALERT_KIND_INSTANT", 0),
    ("HSM_ALERT_KIND_BAR", 1),
    ("HSM_ALERT_KIND_TTL", 2),
    ("HSM_ALERT_COMBINATION_AND", 0),
    ("HSM_ALERT_COMBINATION_OR", 1),
    ("HSM_ALERT_OP_LESS_THAN_OR_EQUAL", 0),
    ("HSM_ALERT_OP_LESS_THAN", 1),
    ("HSM_ALERT_OP_GREATER_THAN", 2),
    ("HSM_ALERT_OP_GREATER_THAN_OR_EQUAL", 3),
    ("HSM_ALERT_OP_EQUAL", 4),
    ("HSM_ALERT_OP_NOT_EQUAL", 5),
    ("HSM_ALERT_OP_IS_CHANGED", 20),
    ("HSM_ALERT_OP_IS_ERROR", 21),
    ("HSM_ALERT_OP_IS_OK", 22),
    ("HSM_ALERT_OP_IS_CHANGED_TO_ERROR", 23),
    ("HSM_ALERT_OP_IS_CHANGED_TO_OK", 24),
    ("HSM_ALERT_OP_CONTAINS", 30),
    ("HSM_ALERT_OP_STARTS_WITH", 31),
    ("HSM_ALERT_OP_ENDS_WITH", 32),
    ("HSM_ALERT_OP_RECEIVED_NEW_VALUE", 50),
    ("HSM_ALERT_PROP_STATUS", 0),
    ("HSM_ALERT_PROP_COMMENT", 1),
    ("HSM_ALERT_PROP_VALUE", 20),
    ("HSM_ALERT_PROP_MIN", 101),
    ("HSM_ALERT_PROP_MAX", 102),
    ("HSM_ALERT_PROP_MEAN", 103),
    ("HSM_ALERT_PROP_COUNT", 104),
    ("HSM_ALERT_PROP_LAST_VALUE", 105),
    ("HSM_ALERT_PROP_FIRST_VALUE", 106),
    ("HSM_ALERT_PROP_LENGTH", 120),
    ("HSM_ALERT_PROP_ORIGINAL_SIZE", 151),
    ("HSM_ALERT_PROP_NEW_SENSOR_DATA", 200),
    ("HSM_ALERT_PROP_EMA_VALUE", 210),
    ("HSM_ALERT_PROP_EMA_MIN", 211),
    ("HSM_ALERT_PROP_EMA_MAX", 212),
    ("HSM_ALERT_PROP_EMA_MEAN", 213),
    ("HSM_ALERT_PROP_EMA_COUNT", 214),
    ("HSM_ALERT_TARGET_CONST", 0),
    ("HSM_ALERT_TARGET_LAST_VALUE", 1),
    ("HSM_ALERT_DESTINATION_NOT_INITIALIZED", 1),
    ("HSM_ALERT_DESTINATION_EMPTY", 2),
    ("HSM_ALERT_DESTINATION_FROM_PARENT", 3),
    ("HSM_ALERT_DESTINATION_ALL_CHATS", 200),
    ("HSM_ALERT_REPEAT_FIVE_MINUTES", 5),
    ("HSM_ALERT_REPEAT_TEN_MINUTES", 6),
    ("HSM_ALERT_REPEAT_FIFTEEN_MINUTES", 7),
    ("HSM_ALERT_REPEAT_THIRTY_MINUTES", 10),
    ("HSM_ALERT_REPEAT_HOURLY", 20),
    ("HSM_ALERT_REPEAT_DAILY", 50),
    ("HSM_ALERT_REPEAT_WEEKLY", 100),
    ("HSM_ALERT_ICON_OK", 0),
    ("HSM_ALERT_ICON_WARNING", 1),
    ("HSM_ALERT_ICON_ERROR", 2),
    ("HSM_ALERT_ICON_PAUSE", 3),
    ("HSM_ALERT_ICON_ARROW_UP", 10),
    ("HSM_ALERT_ICON_ARROW_DOWN", 11),
    ("HSM_ALERT_ICON_CLOCK", 100),
    ("HSM_ALERT_ICON_HOURGLASS", 101),
];

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-env-changed=HSM_COLLECTOR_LIB_DIR");
    println!("cargo:rerun-if-env-changed=HSM_COLLECTOR_INCLUDE_DIR");

    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR"));
    let collector_src = manifest_dir
        .join("../../native/collector")
        .canonicalize()
        .expect("native collector source tree not found (expected src/native/collector)");

    // The headers that belong to the library being linked. With HSM_COLLECTOR_LIB_DIR (a prebuilt
    // collector) they may live elsewhere, so they can be pointed at separately.
    let include_dir = env::var_os("HSM_COLLECTOR_INCLUDE_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| collector_src.join("include"));
    let header = include_dir.join("hsm_collector/hsm_collector.h");
    println!("cargo:rerun-if-changed={}", header.display());

    check_abi_layout(&include_dir, &header);
    emit_layout_constants();
    emit_header_version(&header);

    if let Ok(dir) = env::var("HSM_COLLECTOR_LIB_DIR") {
        emit_link_flags(Path::new(&dir));
        return;
    }

    // Only the collector's own sources matter for a rebuild; CMake itself is incremental.
    println!(
        "cargo:rerun-if-changed={}",
        collector_src.join("src").display()
    );
    println!(
        "cargo:rerun-if-changed={}",
        collector_src.join("include").display()
    );
    println!(
        "cargo:rerun-if-changed={}",
        collector_src.join("CMakeLists.txt").display()
    );

    let build_dir = PathBuf::from(env::var("OUT_DIR").expect("OUT_DIR")).join("collector-build");

    run(Command::new("cmake")
        .arg("-S")
        .arg(&collector_src)
        .arg("-B")
        .arg(&build_dir)
        .arg("-DCMAKE_BUILD_TYPE=Release")
        .arg("-DCMAKE_POSITION_INDEPENDENT_CODE=ON")
        .arg("-DHSM_COLLECTOR_HTTP=ON")
        // The probe needs the library only: no examples, no tests, no install/export rules.
        .arg("-DHSM_COLLECTOR_BUILD_EXAMPLES=OFF")
        .arg("-DHSM_COLLECTOR_BUILD_TESTS=OFF")
        .arg("-DHSM_COLLECTOR_BUILD_BENCH=OFF")
        .arg("-DHSM_COLLECTOR_INSTALL=OFF"));

    run(Command::new("cmake").arg("--build").arg(&build_dir).args([
        "--config",
        "Release",
        "--target",
        "hsm_collector_core",
        "--parallel",
    ]));

    emit_link_flags(&find_lib_dir(&build_dir));
}

/// Compile `static_assert`s over the REAL header, so the C++ compiler answers `sizeof`, `alignof`
/// and `offsetof`.
///
/// This is the half a Rust-only test cannot do: `size_of` on the Rust mirror is measured against a
/// constant in the same crate, so a field appended on the C side changes neither and the mismatch
/// surfaces only at runtime — as C writing past the end of the Rust stack slot for the by-value
/// `*_default()` returns, or reading a field out of adjacent stack bytes.
fn check_abi_layout(include_dir: &Path, header: &Path) {
    assert!(
        header.exists(),
        "collector header not found at {} (set HSM_COLLECTOR_INCLUDE_DIR)",
        header.display()
    );

    let mut source = String::from(
        "// Generated by hsm-collector-sys/build.rs - do not edit.\n\
         #include <cstddef>\n\
         #include <hsm_collector/hsm_collector.h>\n\n",
    );
    for layout in LAYOUTS {
        source.push_str(&format!(
            "static_assert(sizeof({name}) == {size}, \"{name} changed size: update LAYOUTS in \
             hsm-collector-sys/build.rs and the Rust mirror in src/lib.rs\");\n\
             static_assert(alignof({name}) == {align}, \"{name} changed alignment: update \
             hsm-collector-sys\");\n",
            name = layout.name,
            size = layout.size,
            align = layout.align,
        ));
        for (field, offset) in layout.fields {
            source.push_str(&format!(
                "static_assert(offsetof({name}, {field}) == {offset}, \"{name}.{field} moved: \
                 update LAYOUTS in hsm-collector-sys/build.rs and the Rust mirror in \
                 src/lib.rs\");\n",
                name = layout.name,
            ));
        }
        source.push('\n');
    }
    for name in ENUM_TYPES {
        source.push_str(&format!(
            "static_assert(sizeof({name}) == 4, \"{name} is no longer 32-bit: the Rust alias in \
             hsm-collector-sys/src/lib.rs passes it as i32\");\n"
        ));
    }
    for (name, value) in ENUM_VALUES {
        source.push_str(&format!(
            "static_assert({name} == {value}, \"{name} changed value: update ENUM_VALUES in \
             hsm-collector-sys/build.rs and the constant in src/lib.rs\");\n"
        ));
    }

    let out_dir = PathBuf::from(env::var("OUT_DIR").expect("OUT_DIR"));
    let source_path = out_dir.join("abi_layout_check.cpp");
    std::fs::write(&source_path, source).expect("cannot write the ABI layout check");

    // -fsyntax-only: the assertions fire while parsing, so nothing is linked or run (which also
    // keeps the check working when cross-compiling).
    let compiler = env::var("CXX").unwrap_or_else(|_| "c++".to_string());
    let status = Command::new(&compiler)
        .arg("-std=c++17")
        .arg("-fsyntax-only")
        .arg("-I")
        .arg(include_dir)
        .arg(&source_path)
        .status()
        .unwrap_or_else(|err| {
            panic!(
                "cannot run the C++ compiler '{compiler}' for the ABI layout check: {err}. \
                 This crate needs a C++ toolchain anyway (it builds the collector with CMake); \
                 set CXX if yours is named differently."
            )
        });
    assert!(
        status.success(),
        "the collector's C structs no longer match this crate's FFI mirrors (see the failed \
         static_assert above). Update LAYOUTS in hsm-collector-sys/build.rs AND the mirrors in \
         hsm-collector-sys/src/lib.rs — a silent mismatch corrupts the stack at runtime."
    );
}

/// Hand the same expected layout and enum values to the Rust-side tests, so both sides check one
/// source of truth.
fn emit_layout_constants() {
    for layout in LAYOUTS {
        let stem = layout.name.to_uppercase();
        println!("cargo:rustc-env=HSM_ABI_{stem}_SIZE={}", layout.size);
        println!("cargo:rustc-env=HSM_ABI_{stem}_ALIGN={}", layout.align);
    }

    let mut table = String::from(
        "// Generated by hsm-collector-sys/build.rs from ENUM_VALUES - do not edit.\n\
         const ABI_ENUM_VALUES: &[(&str, i64)] = &[\n",
    );
    for (name, value) in ENUM_VALUES {
        table.push_str(&format!("    (\"{name}\", {value}),\n"));
    }
    table.push_str("];\n");
    let out_dir = PathBuf::from(env::var("OUT_DIR").expect("OUT_DIR"));
    std::fs::write(out_dir.join("abi_enum_values.rs"), table)
        .expect("cannot write the enum value table");
}

/// Record the header's compile-time version, so a test can compare it with what the LINKED library
/// reports at runtime. The two come from one tree for an in-tree build, but not necessarily when
/// `HSM_COLLECTOR_LIB_DIR` points at a prebuilt collector.
fn emit_header_version(header: &Path) {
    let text = std::fs::read_to_string(header).expect("cannot read the collector header");
    let component = |macro_name: &str| -> i32 {
        text.lines()
            .find_map(|line| line.trim().strip_prefix(&format!("#define {macro_name} ")))
            .and_then(|value| value.trim().parse().ok())
            .unwrap_or_else(|| panic!("{macro_name} not found in {}", header.display()))
    };
    let packed = component("HSM_COLLECTOR_VERSION_MAJOR") * 10000
        + component("HSM_COLLECTOR_VERSION_MINOR") * 100
        + component("HSM_COLLECTOR_VERSION_PATCH");
    println!("cargo:rustc-env=HSM_COLLECTOR_HEADER_VERSION={packed}");
}

/// Single-config generators drop the archive in the build root; multi-config ones put it under
/// a per-configuration subdirectory.
fn find_lib_dir(build_dir: &Path) -> PathBuf {
    for candidate in [build_dir.to_path_buf(), build_dir.join("Release")] {
        if candidate.join("libhsm_collector_core.a").exists()
            || candidate.join("hsm_collector_core.lib").exists()
        {
            return candidate;
        }
    }
    panic!(
        "hsm_collector_core was not produced under {} — check the CMake build output above",
        build_dir.display()
    );
}

fn emit_link_flags(lib_dir: &Path) {
    println!("cargo:rustc-link-search=native={}", lib_dir.display());
    // Order matters: the archive must come before the libraries that satisfy its undefined symbols.
    println!("cargo:rustc-link-lib=static=hsm_collector_core");
    println!("cargo:rustc-link-lib=dylib=curl");

    // The collector is C++; its runtime is not implied by a static archive. CARGO_CFG_TARGET_OS
    // (not cfg!) is the target — a build script itself is always compiled for the host.
    let target_os = env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    match target_os.as_str() {
        "linux" | "android" => println!("cargo:rustc-link-lib=dylib=stdc++"),
        "macos" | "ios" => println!("cargo:rustc-link-lib=dylib=c++"),
        _ => {}
    }
}

fn run(command: &mut Command) {
    let status = command
        .status()
        .unwrap_or_else(|err| panic!("failed to run {command:?}: {err} (is cmake installed?)"));
    assert!(status.success(), "{command:?} failed with {status}");
}
