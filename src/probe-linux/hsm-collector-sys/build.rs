//! Builds the in-tree native collector (`src/native/collector`) with CMake and links it.
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

fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-env-changed=HSM_COLLECTOR_LIB_DIR");

    // Escape hatch for packaging/CI: point at a prebuilt collector instead of building one here.
    if let Ok(dir) = env::var("HSM_COLLECTOR_LIB_DIR") {
        emit_link_flags(Path::new(&dir));
        return;
    }

    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR"));
    let collector_src = manifest_dir
        .join("../../native/collector")
        .canonicalize()
        .expect("native collector source tree not found (expected src/native/collector)");

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
