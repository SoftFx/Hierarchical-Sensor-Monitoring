use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_void};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::sync::Mutex;
use std::time::Duration;

use hsm_collector_sys as sys;

use crate::error::{Error, Result};
use crate::options::{
    clamp_millis, clamp_millis_i32, CollectorOptions, CollectorStatus, EnumOption, LogLevel,
    SensorOptions,
};
use crate::sensor::{
    BoolSensor, DoubleBarSensor, DoubleSensor, EnumSensor, IntSensor, RawSensor, StringSensor,
    VersionSensor,
};

/// The built-in sensors a host registers one by one (see [`Collector::add_default_sensor`]).
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum DefaultSensor {
    /// `.module/Process <name>/Process CPU`
    ProcessCpu,
    /// `.module/Process <name>/Process memory`
    ProcessMemory,
    /// `.module/Process <name>/Process thread count`
    ProcessThreadCount,
}

impl DefaultSensor {
    fn as_raw(self) -> sys::hsm_default_sensor_t {
        match self {
            DefaultSensor::ProcessCpu => sys::HSM_DEFAULT_PROCESS_CPU,
            DefaultSensor::ProcessMemory => sys::HSM_DEFAULT_PROCESS_MEMORY,
            DefaultSensor::ProcessThreadCount => sys::HSM_DEFAULT_PROCESS_THREAD_COUNT,
        }
    }
}

/// Whether this build bound the Linux metric-source factory (#1414). See
/// [`Collector::install_linux_metric_sources`].
pub const LINUX_METRIC_SOURCES_AVAILABLE: bool = sys::HAS_LINUX_METRIC_SOURCES;

type LogSink = Box<dyn Fn(LogLevel, &str) + Send + Sync + 'static>;

struct LoggerState {
    sink: LogSink,
}

/// The C ABI's log callback.
///
/// # Safety contract
/// Nothing may unwind out of this function: a Rust panic crossing into C++ is undefined behavior.
/// `catch_unwind` converts a panicking sink into a dropped message, which is the same outcome the
/// collector's own swallow-all wrapper produces for a throwing C++ sink.
unsafe extern "C" fn log_trampoline(
    level: sys::hsm_log_level_t,
    message: *const c_char,
    user_data: *mut c_void,
) {
    let _ = catch_unwind(AssertUnwindSafe(|| {
        if user_data.is_null() {
            return;
        }
        // SAFETY: user_data is the `&LoggerState` we registered; the box lives in the Collector
        // and is dropped only after hsm_collector_destroy has returned.
        let state = &*(user_data as *const LoggerState);
        let text = if message.is_null() {
            String::new()
        } else {
            // SAFETY: the collector passes a NUL-terminated UTF-8 message valid for the call.
            CStr::from_ptr(message).to_string_lossy().into_owned()
        };
        (state.sink)(LogLevel::from_raw(level), &text);
    }));
}

/// A native HSM collector instance.
///
/// Owns the underlying `hsm_collector_t` and disposes it on drop. Lifecycle and registration calls
/// are serialized internally: `hsm_collector.h` documents start/stop as "driven from one thread (or
/// serialized externally)", and this wrapper is that external serialization, which is what makes
/// `&self` methods sound to call from several threads.
pub struct Collector {
    handle: *mut sys::hsm_collector_t,
    /// Serializes lifecycle transitions and sensor registration.
    lifecycle: Mutex<()>,
    /// Every logger state ever installed. The C ABI keeps calling a sink until the collector is
    /// destroyed, and a replaced sink may still be running on a worker thread, so nothing is freed
    /// before `Drop`.
    ///
    /// The `Box` is load-bearing and not the redundant indirection clippy takes it for: the C side
    /// holds the address of each state, so the elements must never be relocated by a `Vec` regrow.
    #[allow(clippy::vec_box)]
    loggers: Mutex<Vec<Box<LoggerState>>>,
}

// SAFETY: every method takes `&self` and either (a) calls an entry point hsm_collector.h documents
// as callable from any thread (status, value posting) or (b) takes `lifecycle` first, providing the
// external serialization the header asks for. The handle is a stable pointer for the lifetime of
// the value.
unsafe impl Send for Collector {}
unsafe impl Sync for Collector {}

impl Collector {
    /// Create a collector. Nothing is sent until [`Collector::start`].
    pub fn new(options: &CollectorOptions) -> Result<Self> {
        let access_key = cstring("access_key", &options.access_key)?;
        let server_address = cstring("server_address", &options.server_address)?;
        let client_name = optional_cstring("client_name", options.client_name.as_deref())?;
        let module = optional_cstring("module", options.module.as_deref())?;
        let computer_name = optional_cstring("computer_name", options.computer_name.as_deref())?;

        let mut raw = sys::hsm_collector_options_t {
            access_key: access_key.as_ptr(),
            server_address: server_address.as_ptr(),
            port: i32::from(options.port),
            client_name: as_ptr(&client_name),
            module: as_ptr(&module),
            computer_name: as_ptr(&computer_name),
            allow_plaintext_transport: options.allow_plaintext_transport,
            ..sys::hsm_collector_options_t::default()
        };
        if let Some(period) = options.package_collect_period {
            raw.package_collect_period_ms = clamp_millis_i32(period);
        }
        if let Some(timeout) = options.request_timeout {
            raw.request_timeout_ms = clamp_millis_i32(timeout);
        }
        if let Some(size) = options.max_queue_size {
            raw.max_queue_size = i32::try_from(size).unwrap_or(i32::MAX);
        }
        if let Some(size) = options.max_values_in_package {
            raw.max_values_in_package = i32::try_from(size).unwrap_or(i32::MAX);
        }
        // Always explicit: unlike every other numeric field, the collector does NOT read 0 as
        // "take the default" here (hsm_collector.cpp: `dedup_window_ms_(options.exception_
        // deduplicator_window_ms)` with no `> 0 ?` fallback), and LogError treats a window of 0 as
        // "log every occurrence". Leaving it zeroed would turn a repeated failure -- an unreachable
        // server logs one error per dispatch -- into thousands of identical lines a day in the
        // journal, the probe's log file and `.module/Collector errors`.
        raw.exception_deduplicator_window_ms = clamp_millis(options.exception_deduplicator_window);

        let mut handle: *mut sys::hsm_collector_t = ptr::null_mut();
        // SAFETY: every pointer in `raw` is valid until the call returns, which is all the ABI
        // needs — the collector copies the strings it keeps.
        let code = unsafe { sys::hsm_collector_create(&raw, &mut handle) };
        // The collector copied the key into its own storage, so this wrapper's copy has no further
        // use: wipe it rather than leaving the key in freed heap for the life of the daemon. (Still
        // best-effort -- the collector's own C++ copy is not ours to clear.)
        wipe_cstring(access_key);
        if code != sys::HSM_RESULT_OK || handle.is_null() {
            // No handle yet, so there is no last_error to read. The message would risk echoing the
            // options back anyway, and those contain the access key.
            return Err(Error::from_code("collector create", code, String::new()));
        }

        Ok(Self {
            handle,
            lifecycle: Mutex::new(()),
            loggers: Mutex::new(Vec::new()),
        })
    }

    /// Install a log sink. Both the collector's diagnostics and its deduplicated error messages
    /// arrive here, on collector-owned threads.
    ///
    /// A panicking sink is contained (the message is dropped); it never unwinds into C++.
    pub fn set_logger<F>(&self, sink: F) -> Result<()>
    where
        F: Fn(LogLevel, &str) + Send + Sync + 'static,
    {
        let state = Box::new(LoggerState {
            sink: Box::new(sink),
        });
        let user_data = (&*state as *const LoggerState) as *mut c_void;

        // Keep the state alive before it can be called.
        self.loggers
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .push(state);

        // SAFETY: `user_data` points at a box this collector owns until Drop.
        self.check("set logger", unsafe {
            sys::hsm_collector_set_logger(self.handle, Some(log_trampoline), user_data)
        })
    }

    /// Switch to the real libcurl HTTP transport. Must be called before [`Collector::start`].
    pub fn use_http_transport(&self) -> Result<()> {
        let _guard = self.lock();
        // SAFETY: valid handle; the ABI validates the lifecycle state itself.
        self.check("use http transport", unsafe {
            sys::hsm_collector_use_http_transport(self.handle)
        })
    }

    /// Install the Linux `/proc` metric-source factory so the default host sensors report live
    /// values. Must be called before [`Collector::start`].
    ///
    /// Returns [`Error::Unsupported`] when this build was made without the `linux-default-sensors`
    /// feature — the symbol only exists in a collector that contains workstream 1 (#1414).
    pub fn install_linux_metric_sources(&self) -> Result<()> {
        #[cfg(feature = "linux-default-sensors")]
        {
            let _guard = self.lock();
            // SAFETY: valid handle; the ABI validates the lifecycle state and the platform.
            self.check("install linux metric sources", unsafe {
                sys::hsm_collector_install_linux_metric_sources(self.handle)
            })
        }
        #[cfg(not(feature = "linux-default-sensors"))]
        {
            Err(Error::Unsupported {
                feature: "linux-default-sensors",
            })
        }
    }

    /// Register the module self-sensors (`Service alive`, `Collector version`, `Collector errors`).
    /// These are driven by the collector itself and need no metric source.
    pub fn add_all_module_sensors(&self, product_version: Option<&str>) -> Result<()> {
        let version = optional_cstring("product_version", product_version)?;
        let _guard = self.lock();
        // SAFETY: valid handle; the string outlives the call.
        self.check("add module sensors", unsafe {
            sys::hsm_collector_add_all_module_sensors(self.handle, as_ptr(&version))
        })
    }

    /// Register `.module/Service alive`, `Collector version` and `Collector errors` — the module
    /// group without the process sensors and the product version, for a host that registers those
    /// individually (see [`Collector::add_default_sensor`]).
    pub fn add_collector_monitoring_sensors(&self) -> Result<()> {
        let _guard = self.lock();
        // SAFETY: valid handle.
        self.check("add collector monitoring sensors", unsafe {
            sys::hsm_collector_add_collector_monitoring_sensors(self.handle)
        })
    }

    /// Register one built-in sensor, so a host can pick its set instead of taking a whole group.
    ///
    /// `process_name` fills the `{proc}` segment of the process sensors' `Process <name>` node.
    /// Native hosts pass `None`, which keeps the collector's fixed `Process process` node: the same
    /// path on every host, so HSM alert templates written against it apply everywhere.
    pub fn add_default_sensor(&self, id: DefaultSensor, process_name: Option<&str>) -> Result<()> {
        let name = optional_cstring("process_name", process_name)?;
        // SAFETY: returns a plain value struct.
        let mut params = unsafe { sys::hsm_default_sensor_params_default() };
        params.process_name = as_ptr(&name);

        let _guard = self.lock();
        // SAFETY: valid handle; `params` and the name outlive the call; a NULL out_sensor means the
        // collector keeps the only reference (no handle to release).
        self.check("add default sensor", unsafe {
            sys::hsm_collector_add_default_sensor(
                self.handle,
                id.as_raw(),
                &params,
                ptr::null_mut(),
            )
        })
    }

    /// Register `.module/Version` and return its handle so the host can post its own version.
    ///
    /// The collector only emits that value itself from inside `add_all_module_sensors`, which a
    /// host registering its process sensors individually does not call.
    pub fn product_version_sensor(&self) -> Result<VersionSensor<'_>> {
        let mut handle = ptr::null_mut();
        let _guard = self.lock();
        // SAFETY: valid handle; NULL params take the defaults.
        let code = unsafe {
            sys::hsm_collector_add_default_sensor(
                self.handle,
                sys::HSM_DEFAULT_PRODUCT_VERSION,
                ptr::null(),
                &mut handle,
            )
        };
        self.check("add product version sensor", code)?;
        // SAFETY: the ABI returned OK, so the handle is live.
        Ok(VersionSensor(unsafe { RawSensor::from_raw(handle) }))
    }

    /// The canonical registration JSON of every sensor registered so far, in registration order.
    /// This is exactly what the collector sends to `/commands` at Start.
    pub fn registrations(&self) -> Vec<String> {
        let _guard = self.lock();
        // SAFETY: valid handle; every returned pointer is collector-owned text copied out while
        // the lock prevents a concurrent registration from invalidating it.
        unsafe {
            let count = sys::hsm_collector_registration_count(self.handle);
            (0..count)
                .filter_map(|index| {
                    let mut json = ptr::null();
                    let code =
                        sys::hsm_collector_get_registration_json(self.handle, index, &mut json);
                    (code == sys::HSM_RESULT_OK && !json.is_null())
                        .then(|| CStr::from_ptr(json).to_string_lossy().into_owned())
                })
                .collect()
        }
    }

    /// Register the host default-sensor catalog. Values only flow once a metric-source factory is
    /// installed for this platform.
    pub fn add_all_computer_sensors(&self) -> Result<()> {
        let _guard = self.lock();
        // SAFETY: valid handle.
        self.check("add computer sensors", unsafe {
            sys::hsm_collector_add_all_computer_sensors(self.handle)
        })
    }

    /// Register the queue self-diagnostics (overflow, package size, process time).
    pub fn add_all_queue_diagnostic_sensors(&self) -> Result<()> {
        let _guard = self.lock();
        // SAFETY: valid handle.
        self.check("add queue diagnostic sensors", unsafe {
            sys::hsm_collector_add_all_queue_diagnostic_sensors(self.handle)
        })
    }

    pub fn double_sensor(&self, path: &str, options: &SensorOptions) -> Result<DoubleSensor<'_>> {
        Ok(DoubleSensor(self.instant_sensor(
            path,
            sys::HSM_SENSOR_TYPE_DOUBLE,
            options,
        )?))
    }

    pub fn int_sensor(&self, path: &str, options: &SensorOptions) -> Result<IntSensor<'_>> {
        Ok(IntSensor(self.instant_sensor(
            path,
            sys::HSM_SENSOR_TYPE_INT,
            options,
        )?))
    }

    pub fn bool_sensor(&self, path: &str, options: &SensorOptions) -> Result<BoolSensor<'_>> {
        Ok(BoolSensor(self.instant_sensor(
            path,
            sys::HSM_SENSOR_TYPE_BOOLEAN,
            options,
        )?))
    }

    pub fn string_sensor(&self, path: &str, options: &SensorOptions) -> Result<StringSensor<'_>> {
        Ok(StringSensor(self.instant_sensor(
            path,
            sys::HSM_SENSOR_TYPE_STRING,
            options,
        )?))
    }

    /// Enum sensor with its option set registered once, at creation.
    pub fn enum_sensor(
        &self,
        path: &str,
        description: Option<&str>,
        options: &[EnumOption],
    ) -> Result<EnumSensor<'_>> {
        let path = cstring("sensor path", path)?;
        let description = optional_cstring("sensor description", description)?;

        // The C strings must outlive the array of borrowed pointers below.
        let mut values = Vec::with_capacity(options.len());
        let mut descriptions = Vec::with_capacity(options.len());
        for option in options {
            values.push(cstring("enum option value", &option.value)?);
            descriptions.push(optional_cstring(
                "enum option description",
                option.description.as_deref(),
            )?);
        }
        let raw_options: Vec<sys::hsm_enum_option_t> = options
            .iter()
            .enumerate()
            .map(|(index, option)| sys::hsm_enum_option_t {
                key: option.key,
                value: values[index].as_ptr(),
                color: option.color,
                description: as_ptr(&descriptions[index]),
            })
            .collect();

        let mut handle = ptr::null_mut();
        let _guard = self.lock();
        // SAFETY: path/description/options all outlive the call; the collector copies what it keeps.
        let code = unsafe {
            sys::hsm_collector_create_enum_sensor_with_options(
                self.handle,
                path.as_ptr(),
                as_ptr(&description),
                raw_options.as_ptr(),
                raw_options.len(),
                &mut handle,
            )
        };
        self.check("create enum sensor", code)?;
        // SAFETY: the ABI returned OK, so the handle is a live sensor of this collector.
        Ok(EnumSensor(unsafe { RawSensor::from_raw(handle) }))
    }

    /// `DoubleBar` sensor aggregating into `bar_period` windows.
    pub fn double_bar_sensor(
        &self,
        path: &str,
        bar_period: Duration,
        post_period: Duration,
        precision: i32,
        options: &SensorOptions,
    ) -> Result<DoubleBarSensor<'_>> {
        let path = cstring("sensor path", path)?;
        let description = optional_cstring("sensor description", options.description.as_deref())?;

        // SAFETY: returns a plain value struct; no pointers are dereferenced.
        let mut raw = unsafe { sys::hsm_sensor_options_default() };
        options.apply(&mut raw);
        raw.description = as_ptr(&description);

        let mut handle = ptr::null_mut();
        let _guard = self.lock();
        // SAFETY: path/description outlive the call.
        let code = unsafe {
            sys::hsm_collector_create_double_bar_sensor_with_options(
                self.handle,
                path.as_ptr(),
                clamp_millis(bar_period),
                clamp_millis(post_period),
                precision,
                &raw,
                &mut handle,
            )
        };
        self.check("create double bar sensor", code)?;
        // SAFETY: the ABI returned OK, so the handle is live.
        Ok(DoubleBarSensor(unsafe { RawSensor::from_raw(handle) }))
    }

    fn instant_sensor(
        &self,
        path: &str,
        sensor_type: sys::hsm_sensor_type_t,
        options: &SensorOptions,
    ) -> Result<RawSensor<'_>> {
        let path = cstring("sensor path", path)?;
        let description = optional_cstring("sensor description", options.description.as_deref())?;

        // SAFETY: returns a plain value struct.
        let mut raw = unsafe { sys::hsm_sensor_options_default() };
        options.apply(&mut raw);
        raw.description = as_ptr(&description);

        let mut handle = ptr::null_mut();
        let _guard = self.lock();
        // SAFETY: path/description outlive the call.
        let code = unsafe {
            sys::hsm_collector_create_sensor_with_options(
                self.handle,
                path.as_ptr(),
                sensor_type,
                &raw,
                &mut handle,
            )
        };
        self.check("create sensor", code)?;
        // SAFETY: the ABI returned OK, so the handle is live.
        Ok(unsafe { RawSensor::from_raw(handle) })
    }

    /// Start the collector: register every sensor and begin dispatching.
    pub fn start(&self) -> Result<()> {
        let _guard = self.lock();
        // SAFETY: valid handle; serialized by `lifecycle`.
        self.check("collector start", unsafe {
            sys::hsm_collector_start(self.handle)
        })
    }

    /// Stop the collector. The drain is bounded internally, so this cannot block a host restart.
    pub fn stop(&self) -> Result<()> {
        let _guard = self.lock();
        // SAFETY: valid handle; serialized by `lifecycle`.
        self.check("collector stop", unsafe {
            sys::hsm_collector_stop(self.handle)
        })
    }

    /// Terminal, idempotent shutdown. Called automatically on drop.
    pub fn dispose(&self) {
        let _guard = self.lock();
        // SAFETY: valid handle; dispose never fails and is callable from any state.
        unsafe { sys::hsm_collector_dispose(self.handle) };
    }

    pub fn status(&self) -> CollectorStatus {
        // SAFETY: hsm_collector.h documents status as safe from any thread and any state.
        CollectorStatus::from_raw(unsafe { sys::hsm_collector_status(self.handle) })
    }

    /// Whether the sender reports the server reachable. Callable in any lifecycle state.
    pub fn test_connection(&self) -> Result<()> {
        // SAFETY: valid handle; documented as callable in any state.
        self.check("test connection", unsafe {
            sys::hsm_collector_test_connection(self.handle)
        })
    }

    /// The collector's last recorded error text.
    pub fn last_error(&self) -> String {
        // SAFETY: valid handle; the ABI never returns NULL here, and the text is owned by the
        // collector and copied out before any further call can replace it.
        unsafe {
            let text = sys::hsm_collector_last_error(self.handle);
            if text.is_null() {
                String::new()
            } else {
                CStr::from_ptr(text).to_string_lossy().into_owned()
            }
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, ()> {
        // A poisoned lifecycle lock means some other thread panicked mid-transition. The collector
        // itself is unaffected (its state lives in C++), so recovering is strictly better than
        // turning one panic into a cascade of them in a long-running daemon.
        self.lifecycle
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn check(&self, operation: &'static str, code: sys::hsm_result_t) -> Result<()> {
        if code == sys::HSM_RESULT_OK {
            Ok(())
        } else {
            Err(Error::from_code(operation, code, self.last_error()))
        }
    }
}

impl Drop for Collector {
    fn drop(&mut self) {
        // SAFETY: dispose is the graceful terminal transition (it stops a running collector and
        // joins its threads, so no callback can be in flight afterwards); destroy then frees the
        // handle. Both are called exactly once. The logger boxes are dropped after this function
        // returns, i.e. strictly after the last possible callback.
        unsafe {
            sys::hsm_collector_dispose(self.handle);
            sys::hsm_collector_destroy(self.handle);
        }
    }
}

/// Overwrite a C string's bytes before it is freed. Used for the access key, which this wrapper
/// copies out of [`CollectorOptions`] to hand to the ABI.
fn wipe_cstring(value: CString) {
    let mut bytes = value.into_bytes_with_nul();
    for byte in bytes.iter_mut() {
        // SAFETY-adjacent: write_volatile keeps the compiler from eliding a write to a buffer that
        // is about to be dropped.
        unsafe { std::ptr::write_volatile(byte, 0) };
    }
}

fn cstring(field: &'static str, value: &str) -> Result<CString> {
    CString::new(value).map_err(|err| Error::nul(field, err))
}

fn optional_cstring(field: &'static str, value: Option<&str>) -> Result<Option<CString>> {
    value.map(|text| cstring(field, text)).transpose()
}

fn as_ptr(value: &Option<CString>) -> *const c_char {
    value.as_ref().map_or(ptr::null(), |text| text.as_ptr())
}
