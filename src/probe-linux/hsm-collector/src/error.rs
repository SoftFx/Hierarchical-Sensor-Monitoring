use std::ffi::NulError;
use std::fmt;

use hsm_collector_sys as sys;

/// A failure crossing the collector's C ABI.
///
/// Error text never carries caller-supplied secrets: the only interpolated strings are the
/// operation name and the collector's own `hsm_collector_last_error` message.
#[derive(Debug)]
pub enum Error {
    /// The ABI call returned a non-OK result code.
    Abi {
        operation: &'static str,
        code: i32,
        /// The collector's last-error text, when a collector handle was available to ask.
        message: String,
    },
    /// A Rust string destined for a C `const char*` contained an interior NUL byte.
    InteriorNul { field: &'static str },
    /// The linked collector does not provide this entry point in the current build configuration.
    Unsupported { feature: &'static str },
}

impl Error {
    pub(crate) fn from_code(operation: &'static str, code: i32, message: String) -> Self {
        Error::Abi {
            operation,
            code,
            message,
        }
    }

    pub(crate) fn nul(field: &'static str, _source: NulError) -> Self {
        // The source value is deliberately dropped: it may be an access key or another secret.
        Error::InteriorNul { field }
    }

    /// The raw ABI result code, when this error came from one.
    pub fn code(&self) -> Option<i32> {
        match self {
            Error::Abi { code, .. } => Some(*code),
            _ => None,
        }
    }
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Error::Abi {
                operation,
                code,
                message,
            } => {
                write!(f, "{operation} failed: {} ({code})", describe(*code))?;
                if !message.is_empty() {
                    write!(f, ": {message}")?;
                }
                Ok(())
            }
            Error::InteriorNul { field } => {
                write!(
                    f,
                    "{field} contains an interior NUL byte and cannot cross the C ABI"
                )
            }
            Error::Unsupported { feature } => {
                write!(
                    f,
                    "the linked collector was built without support for '{feature}'"
                )
            }
        }
    }
}

impl std::error::Error for Error {}

fn describe(code: i32) -> &'static str {
    match code {
        sys::HSM_RESULT_OK => "ok",
        sys::HSM_RESULT_INVALID_ARGUMENT => "invalid argument",
        sys::HSM_RESULT_INVALID_STATE => "invalid state",
        sys::HSM_RESULT_NOT_FOUND => "not found",
        sys::HSM_RESULT_LIMIT_EXCEEDED => "limit exceeded",
        sys::HSM_RESULT_INTERNAL_ERROR => "internal error",
        _ => "unknown result code",
    }
}

pub type Result<T> = std::result::Result<T, Error>;
