//! Raw FFI declarations for the subset of the HSM native collector's stable C ABI that the Linux
//! probe uses (`src/native/collector/include/hsm_collector/hsm_collector.h`).
//!
//! The declarations are hand-written rather than generated: the subset is small, it is reviewed
//! against the header by eye, and a diff in this file is a readable statement about which part of
//! the ABI the probe depends on. Every item keeps the exact C name and the exact struct layout —
//! adding, removing or reordering a field here is an ABI break even though the Rust compiler
//! cannot see it.
//!
//! This crate is `unsafe` by nature and adds no safety of its own; use `hsm-collector` instead.

#![allow(non_camel_case_types)]

use std::os::raw::{c_char, c_void};

/// Opaque collector instance.
#[repr(C)]
pub struct hsm_collector_t {
    _private: [u8; 0],
}

/// Opaque sensor handle. Owned by the collector; `hsm_sensor_release` frees only the handle.
#[repr(C)]
pub struct hsm_sensor_t {
    _private: [u8; 0],
}

pub type hsm_result_t = i32;
pub const HSM_RESULT_OK: hsm_result_t = 0;
pub const HSM_RESULT_INVALID_ARGUMENT: hsm_result_t = 1;
pub const HSM_RESULT_INVALID_STATE: hsm_result_t = 2;
pub const HSM_RESULT_NOT_FOUND: hsm_result_t = 3;
pub const HSM_RESULT_LIMIT_EXCEEDED: hsm_result_t = 4;
pub const HSM_RESULT_INTERNAL_ERROR: hsm_result_t = 255;

pub type hsm_collector_status_t = i32;
pub const HSM_COLLECTOR_STATUS_STOPPED: hsm_collector_status_t = 0;
pub const HSM_COLLECTOR_STATUS_STARTING: hsm_collector_status_t = 1;
pub const HSM_COLLECTOR_STATUS_RUNNING: hsm_collector_status_t = 2;
pub const HSM_COLLECTOR_STATUS_STOPPING: hsm_collector_status_t = 3;
pub const HSM_COLLECTOR_STATUS_DISPOSED: hsm_collector_status_t = 4;

pub type hsm_sensor_status_t = i32;
pub const HSM_SENSOR_STATUS_OFF_TIME: hsm_sensor_status_t = 0;
pub const HSM_SENSOR_STATUS_OK: hsm_sensor_status_t = 1;
pub const HSM_SENSOR_STATUS_WARNING: hsm_sensor_status_t = 2;
pub const HSM_SENSOR_STATUS_ERROR: hsm_sensor_status_t = 3;

pub type hsm_sensor_type_t = i32;
pub const HSM_SENSOR_TYPE_BOOLEAN: hsm_sensor_type_t = 0;
pub const HSM_SENSOR_TYPE_INT: hsm_sensor_type_t = 1;
pub const HSM_SENSOR_TYPE_DOUBLE: hsm_sensor_type_t = 2;
pub const HSM_SENSOR_TYPE_STRING: hsm_sensor_type_t = 3;
pub const HSM_SENSOR_TYPE_INT_BAR: hsm_sensor_type_t = 4;
pub const HSM_SENSOR_TYPE_DOUBLE_BAR: hsm_sensor_type_t = 5;
pub const HSM_SENSOR_TYPE_ENUM: hsm_sensor_type_t = 10;

pub type hsm_log_level_t = i32;
pub const HSM_LOG_LEVEL_DEBUG: hsm_log_level_t = 0;
pub const HSM_LOG_LEVEL_INFO: hsm_log_level_t = 1;
pub const HSM_LOG_LEVEL_ERROR: hsm_log_level_t = 2;

/// Mirrors `hsm_collector_options_t`. Every numeric field uses 0 for "take the collector default".
#[repr(C)]
#[derive(Clone, Copy)]
pub struct hsm_collector_options_t {
    pub access_key: *const c_char,
    pub server_address: *const c_char,
    pub port: i32,
    pub client_name: *const c_char,
    pub module: *const c_char,
    pub computer_name: *const c_char,

    pub max_queue_size: i32,
    pub max_values_in_package: i32,
    pub package_collect_period_ms: i32,
    pub request_timeout_ms: i32,
    pub max_sensors: i32,

    pub allow_untrusted_server_certificate: bool,
    pub allow_plaintext_transport: bool,

    pub exception_deduplicator_window_ms: i64,
    pub max_deduplicated_messages: i32,
}

impl Default for hsm_collector_options_t {
    fn default() -> Self {
        // A zeroed struct is the documented "all defaults" value for this struct specifically
        // (unlike hsm_sensor_options_t, whose tri-states need -1 sentinels).
        Self {
            access_key: std::ptr::null(),
            server_address: std::ptr::null(),
            port: 0,
            client_name: std::ptr::null(),
            module: std::ptr::null(),
            computer_name: std::ptr::null(),
            max_queue_size: 0,
            max_values_in_package: 0,
            package_collect_period_ms: 0,
            request_timeout_ms: 0,
            max_sensors: 0,
            allow_untrusted_server_certificate: false,
            allow_plaintext_transport: false,
            exception_deduplicator_window_ms: 0,
            max_deduplicated_messages: 0,
        }
    }
}

/// Mirrors `hsm_sensor_options_t`.
///
/// NEVER zero-initialize this: the tri-state fields use `-1` for "emit null / take the managed
/// default" and `0` means an explicit `false`. Always start from [`hsm_sensor_options_default`].
#[repr(C)]
#[derive(Clone, Copy)]
pub struct hsm_sensor_options_t {
    pub ttl_ms: i64,
    pub unit: i32,
    pub description: *const c_char,
    pub keep_history_ms: i64,
    pub self_destroy_ms: i64,
    pub display_unit: i32,
    pub statistics: i32,
    pub is_singleton: i32,
    pub aggregate_data: i32,
    pub enable_grafana: i32,
    pub is_computer_sensor: bool,
    pub sensor_location: i32,
    pub default_alert_options: i64,
}

/// Mirrors `hsm_enum_option_t`.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct hsm_enum_option_t {
    pub key: i32,
    pub value: *const c_char,
    pub color: i32,
    pub description: *const c_char,
}

/// Log sink callback. The collector wraps it swallow-all, but a Rust panic unwinding into C++ is
/// undefined behavior regardless — every implementation must be wrapped in `catch_unwind`.
pub type hsm_log_callback_t = Option<
    unsafe extern "C" fn(level: hsm_log_level_t, message: *const c_char, user_data: *mut c_void),
>;

/// Lifecycle observer callback. Fires under the collector's lifecycle lock: it must not call
/// start/stop/dispose, and must not unwind.
pub type hsm_lifecycle_callback_t =
    Option<unsafe extern "C" fn(status: hsm_collector_status_t, user_data: *mut c_void)>;

extern "C" {
    /// Packed `MAJOR*10000 + MINOR*100 + PATCH` of the linked library.
    pub fn hsm_collector_version() -> i32;

    pub fn hsm_collector_create(
        options: *const hsm_collector_options_t,
        out_collector: *mut *mut hsm_collector_t,
    ) -> hsm_result_t;
    pub fn hsm_collector_destroy(collector: *mut hsm_collector_t);
    pub fn hsm_collector_dispose(collector: *mut hsm_collector_t);

    pub fn hsm_collector_start(collector: *mut hsm_collector_t) -> hsm_result_t;
    pub fn hsm_collector_stop(collector: *mut hsm_collector_t) -> hsm_result_t;
    pub fn hsm_collector_status(collector: *const hsm_collector_t) -> hsm_collector_status_t;
    pub fn hsm_collector_test_connection(collector: *mut hsm_collector_t) -> hsm_result_t;

    /// Switch from the in-memory recording sender to the real libcurl transport. Call before Start.
    pub fn hsm_collector_use_http_transport(collector: *mut hsm_collector_t) -> hsm_result_t;

    pub fn hsm_collector_set_logger(
        collector: *mut hsm_collector_t,
        callback: hsm_log_callback_t,
        user_data: *mut c_void,
    ) -> hsm_result_t;
    pub fn hsm_collector_add_lifecycle_listener(
        collector: *mut hsm_collector_t,
        callback: hsm_lifecycle_callback_t,
        user_data: *mut c_void,
    ) -> hsm_result_t;

    /// Module self-sensors (`.module/Service alive`, `Collector version`, `Collector errors`, …).
    /// Platform-free: they are driven by the collector itself, not by a metric source.
    pub fn hsm_collector_add_all_module_sensors(
        collector: *mut hsm_collector_t,
        product_version: *const c_char,
    ) -> hsm_result_t;
    /// Host catalog (`.computer/…`). The registration text is platform-agnostic; live values come
    /// from the installed metric-source factory.
    pub fn hsm_collector_add_all_computer_sensors(collector: *mut hsm_collector_t) -> hsm_result_t;
    pub fn hsm_collector_add_all_queue_diagnostic_sensors(
        collector: *mut hsm_collector_t,
    ) -> hsm_result_t;

    pub fn hsm_collector_create_int_sensor(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;
    pub fn hsm_collector_create_bool_sensor(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;
    pub fn hsm_collector_create_double_sensor(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;
    pub fn hsm_collector_create_string_sensor(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;

    /// Options value pre-filled with the collector defaults; the only correct starting point for
    /// [`hsm_sensor_options_t`].
    pub fn hsm_sensor_options_default() -> hsm_sensor_options_t;

    pub fn hsm_collector_create_sensor_with_options(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        sensor_type: hsm_sensor_type_t,
        options: *const hsm_sensor_options_t,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;

    pub fn hsm_collector_create_enum_sensor_with_options(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        description: *const c_char,
        enum_options: *const hsm_enum_option_t,
        enum_option_count: usize,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;

    pub fn hsm_collector_create_double_bar_sensor_with_options(
        collector: *mut hsm_collector_t,
        path: *const c_char,
        bar_period_ms: i64,
        post_period_ms: i64,
        precision: i32,
        options: *const hsm_sensor_options_t,
        out_sensor: *mut *mut hsm_sensor_t,
    ) -> hsm_result_t;

    pub fn hsm_sensor_release(sensor: *mut hsm_sensor_t);

    pub fn hsm_sensor_add_int(
        sensor: *mut hsm_sensor_t,
        value: i32,
        status: hsm_sensor_status_t,
        comment: *const c_char,
    ) -> hsm_result_t;
    pub fn hsm_sensor_add_bool(
        sensor: *mut hsm_sensor_t,
        value: bool,
        status: hsm_sensor_status_t,
        comment: *const c_char,
    ) -> hsm_result_t;
    pub fn hsm_sensor_add_double(
        sensor: *mut hsm_sensor_t,
        value: f64,
        status: hsm_sensor_status_t,
        comment: *const c_char,
    ) -> hsm_result_t;
    pub fn hsm_sensor_add_string(
        sensor: *mut hsm_sensor_t,
        value: *const c_char,
        status: hsm_sensor_status_t,
        comment: *const c_char,
    ) -> hsm_result_t;
    pub fn hsm_sensor_add_enum(
        sensor: *mut hsm_sensor_t,
        value: i32,
        status: hsm_sensor_status_t,
        comment: *const c_char,
    ) -> hsm_result_t;
    pub fn hsm_sensor_add_bar_double(sensor: *mut hsm_sensor_t, value: f64) -> hsm_result_t;

    /// Last error text recorded on the collector. Valid until the next failing call on the same
    /// collector; never NULL.
    pub fn hsm_collector_last_error(collector: *const hsm_collector_t) -> *const c_char;
}

#[cfg(feature = "linux-default-sensors")]
extern "C" {
    /// Install the Linux `/proc`-based metric-source factory so the value-typed default sensors
    /// (Total CPU, Free RAM, free disk, process counters) read live values. Call before Start.
    ///
    /// TODO(#1414): this symbol is added by workstream 1 and is absent from master's collector,
    /// which is why the whole block is behind the `linux-default-sensors` feature. Linking with
    /// the feature on against an older collector fails at link time — by design, so the gap is
    /// never silent.
    pub fn hsm_collector_install_linux_metric_sources(
        collector: *mut hsm_collector_t,
    ) -> hsm_result_t;
}

/// Whether the crate was built with the Linux default-sensor factory bound (#1414).
pub const HAS_LINUX_METRIC_SOURCES: bool = cfg!(feature = "linux-default-sensors");

/// Layout guards for the three structs the ABI passes by value/pointer. They are the only place a
/// silent field insertion on either side can be caught, since the Rust compiler never sees the C
/// header. The expected sizes are the 64-bit C layout (8-byte pointers, natural alignment) worked
/// out field by field from `hsm_collector.h`.
#[cfg(all(test, target_pointer_width = "64"))]
mod layout_tests {
    use super::*;
    use std::mem::{align_of, size_of};

    #[test]
    fn collector_options_layout_matches_the_c_struct() {
        // ptr,ptr,i32+pad,ptr,ptr,ptr = 48 | 5×i32 = 20 | 2×bool = 2 (+2 pad)
        // | i64 @72 | i32 @80 (+4 tail pad)
        assert_eq!(size_of::<hsm_collector_options_t>(), 88);
        assert_eq!(align_of::<hsm_collector_options_t>(), 8);
    }

    #[test]
    fn sensor_options_layout_matches_the_c_struct() {
        // i64 | i32+pad | ptr | i64 | i64 | 5×i32 | bool @60 (+3 pad) | i32 @64 | i64 @72
        assert_eq!(size_of::<hsm_sensor_options_t>(), 80);
        assert_eq!(align_of::<hsm_sensor_options_t>(), 8);
    }

    #[test]
    fn enum_option_layout_matches_the_c_struct() {
        // i32+pad | ptr | i32+pad | ptr
        assert_eq!(size_of::<hsm_enum_option_t>(), 32);
        assert_eq!(align_of::<hsm_enum_option_t>(), 8);
    }

    #[test]
    fn linked_library_is_the_abi_this_crate_declares() {
        // MAJOR*10000 + MINOR*100 + PATCH. The probe binds a 0.x ABI; a MAJOR bump is a breaking
        // change that must be reviewed here rather than discovered at runtime.
        let packed = unsafe { hsm_collector_version() };
        assert_eq!(
            packed / 10000,
            0,
            "unexpected collector MAJOR version: {packed}"
        );
    }
}
