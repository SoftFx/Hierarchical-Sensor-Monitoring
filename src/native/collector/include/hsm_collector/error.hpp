#pragma once

/// @file
/// @brief Exception type and result-code helpers for the C++ wrapper.

#include "hsm_collector/hsm_collector.h"

#include <stdexcept>
#include <string>

namespace hsm::collector
{
    /// Thrown by every wrapper call that fails. The message carries the collector's last error
    /// (for collector-scoped calls) or a static description plus the C result code name.
    class Error : public std::runtime_error
    {
    public:
        explicit Error(const std::string& message)
            : std::runtime_error(message)
        {
        }
    };

    namespace detail
    {
        /// Human-readable name for a C result code, appended to sensor-scoped error messages
        /// (sensor calls have no collector handle to pull a last-error string from).
        inline const char* ResultName(hsm_result_t result)
        {
            switch (result)
            {
            case HSM_RESULT_OK:
                return "OK";
            case HSM_RESULT_INVALID_ARGUMENT:
                return "INVALID_ARGUMENT";
            case HSM_RESULT_INVALID_STATE:
                return "INVALID_STATE";
            case HSM_RESULT_NOT_FOUND:
                return "NOT_FOUND";
            case HSM_RESULT_LIMIT_EXCEEDED:
                return "LIMIT_EXCEEDED";
            case HSM_RESULT_INTERNAL_ERROR:
            default:
                return "INTERNAL_ERROR";
            }
        }

        /// Throw Error(context + result name) when a sensor-scoped C call fails.
        inline void ThrowIfFailed(hsm_result_t result, const char* context)
        {
            if (result != HSM_RESULT_OK)
                throw Error(std::string{ context } + " (" + ResultName(result) + ")");
        }
    } // namespace detail
} // namespace hsm::collector
