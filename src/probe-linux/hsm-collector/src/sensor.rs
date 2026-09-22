use std::ffi::CString;
use std::marker::PhantomData;
use std::ptr;

use hsm_collector_sys as sys;

use crate::error::{Error, Result};
use crate::options::SensorStatus;
use crate::Collector;

/// An owned sensor handle.
///
/// The collector owns the sensor itself; this handle is only a reference, and `Drop` releases the
/// handle alone. The `'c` lifetime ties it to the collector so a handle can never outlive the
/// instance that registered it — which the C ABI would treat as use-after-free.
pub(crate) struct RawSensor<'c> {
    handle: *mut sys::hsm_sensor_t,
    collector: PhantomData<&'c Collector>,
}

impl std::fmt::Debug for RawSensor<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        // The path is not readable back through the ABI, so the handle address is all there is.
        write!(f, "RawSensor({:p})", self.handle)
    }
}

// SAFETY: hsm_collector.h documents the value-posting entry points as callable from any thread
// (the same contract as the managed collector, root CLAUDE.md invariant #5): AddValue enqueues
// under the collector's own synchronization. The handle is an opaque pointer into collector-owned
// storage whose lifetime is pinned by the `'c` borrow, so moving or sharing it across threads does
// not change what the ABI sees.
unsafe impl Send for RawSensor<'_> {}
unsafe impl Sync for RawSensor<'_> {}

impl RawSensor<'_> {
    /// # Safety
    /// `handle` must be a non-null sensor handle obtained from `collector`, not yet released.
    pub(crate) unsafe fn from_raw(handle: *mut sys::hsm_sensor_t) -> Self {
        Self {
            handle,
            collector: PhantomData,
        }
    }

    pub(crate) fn as_ptr(&self) -> *mut sys::hsm_sensor_t {
        self.handle
    }
}

impl Drop for RawSensor<'_> {
    fn drop(&mut self) {
        // SAFETY: the handle came from the ABI and is released exactly once (RawSensor is not
        // Clone/Copy). The collector outlives us by construction of the `'c` borrow.
        unsafe { sys::hsm_sensor_release(self.handle) };
    }
}

/// Converts an optional comment into a C string that lives for the duration of the call.
fn comment_cstring(comment: Option<&str>) -> Result<Option<CString>> {
    match comment {
        None => Ok(None),
        Some(text) => CString::new(text)
            .map(Some)
            .map_err(|err| Error::nul("comment", err)),
    }
}

fn comment_ptr(comment: &Option<CString>) -> *const std::os::raw::c_char {
    comment.as_ref().map_or(ptr::null(), |c| c.as_ptr())
}

macro_rules! typed_sensor {
    (
        $(#[$meta:meta])*
        $name:ident, $value:ty, $add:ident, $operation:literal
    ) => {
        $(#[$meta])*
        #[derive(Debug)]
        pub struct $name<'c>(pub(crate) RawSensor<'c>);

        impl $name<'_> {
            /// Post a value with `Ok` status and no comment.
            pub fn add(&self, value: $value) -> Result<()> {
                self.add_with(value, SensorStatus::Ok, None)
            }

            /// Post a value with an explicit status and optional comment.
            pub fn add_with(
                &self,
                value: $value,
                status: SensorStatus,
                comment: Option<&str>,
            ) -> Result<()> {
                let comment = comment_cstring(comment)?;
                // SAFETY: the handle is live for `&self`, and both pointers are valid for the
                // duration of the call (the CString outlives it).
                let code = unsafe {
                    sys::$add(
                        self.0.as_ptr(),
                        value,
                        status.as_raw(),
                        comment_ptr(&comment),
                    )
                };
                if code == sys::HSM_RESULT_OK {
                    Ok(())
                } else {
                    Err(Error::from_code($operation, code, String::new()))
                }
            }
        }
    };
}

typed_sensor!(
    /// Instant `Double` sensor.
    DoubleSensor, f64, hsm_sensor_add_double, "add double value"
);
typed_sensor!(
    /// Instant `Int` sensor.
    IntSensor, i32, hsm_sensor_add_int, "add int value"
);
typed_sensor!(
    /// Instant `Boolean` sensor.
    BoolSensor, bool, hsm_sensor_add_bool, "add bool value"
);
typed_sensor!(
    /// Instant `Enum` sensor. The posted value is an option key registered at creation.
    EnumSensor, i32, hsm_sensor_add_enum, "add enum value"
);

/// Instant `String` sensor.
#[derive(Debug)]
pub struct StringSensor<'c>(pub(crate) RawSensor<'c>);

impl StringSensor<'_> {
    pub fn add(&self, value: &str) -> Result<()> {
        self.add_with(value, SensorStatus::Ok, None)
    }

    pub fn add_with(&self, value: &str, status: SensorStatus, comment: Option<&str>) -> Result<()> {
        let value = CString::new(value).map_err(|err| Error::nul("string value", err))?;
        let comment = comment_cstring(comment)?;
        // SAFETY: handle live for `&self`; both C strings outlive the call.
        let code = unsafe {
            sys::hsm_sensor_add_string(
                self.0.as_ptr(),
                value.as_ptr(),
                status.as_raw(),
                comment_ptr(&comment),
            )
        };
        if code == sys::HSM_RESULT_OK {
            Ok(())
        } else {
            Err(Error::from_code("add string value", code, String::new()))
        }
    }
}

/// `DoubleBar` sensor. Values accumulate into the bar window; the collector publishes the bar when
/// the window rolls and flushes a partial bar on stop.
#[derive(Debug)]
pub struct DoubleBarSensor<'c>(pub(crate) RawSensor<'c>);

impl DoubleBarSensor<'_> {
    /// Accumulate one sample. Non-finite values are silently skipped by the collector.
    pub fn add(&self, value: f64) -> Result<()> {
        // SAFETY: handle live for `&self`.
        let code = unsafe { sys::hsm_sensor_add_bar_double(self.0.as_ptr(), value) };
        if code == sys::HSM_RESULT_OK {
            Ok(())
        } else {
            Err(Error::from_code("add bar value", code, String::new()))
        }
    }
}
