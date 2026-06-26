#pragma once

/// @file
/// @brief Built-in (default) sensor catalog: the C++ mirror of `hsm_default_sensor_t` and its
/// deterministic path/alert substitutions.

#include "hsm_collector/hsm_collector.h"

#include <optional>
#include <string>

namespace hsm::collector
{
    /// One built-in sensor of the managed IWindowsCollection/IUnixCollection surface. Each maps 1:1
    /// to a managed prototype and registers a byte-identical AddOrUpdateSensorRequest.
    enum class DefaultSensor
    {
        ProcessCpu = HSM_DEFAULT_PROCESS_CPU,
        ProcessMemory = HSM_DEFAULT_PROCESS_MEMORY,
        ProcessThreadCount = HSM_DEFAULT_PROCESS_THREAD_COUNT,
        ProcessThreadPoolThreadCount = HSM_DEFAULT_PROCESS_THREADPOOL_THREAD_COUNT,

        TotalCpu = HSM_DEFAULT_TOTAL_CPU,
        FreeRamMemory = HSM_DEFAULT_FREE_RAM_MEMORY,

        FreeDiskSpace = HSM_DEFAULT_FREE_DISK_SPACE,
        FreeDiskSpacePrediction = HSM_DEFAULT_FREE_DISK_SPACE_PREDICTION,
        ActiveDiskTime = HSM_DEFAULT_ACTIVE_DISK_TIME,
        DiskQueueLength = HSM_DEFAULT_DISK_QUEUE_LENGTH,
        DiskAverageWriteSpeed = HSM_DEFAULT_DISK_AVERAGE_WRITE_SPEED,

        WindowsLastRestart = HSM_DEFAULT_WINDOWS_LAST_RESTART,
        WindowsInstallDate = HSM_DEFAULT_WINDOWS_INSTALL_DATE,
        WindowsLastUpdate = HSM_DEFAULT_WINDOWS_LAST_UPDATE,
        WindowsVersion = HSM_DEFAULT_WINDOWS_VERSION,

        WindowsApplicationErrorLogs = HSM_DEFAULT_WINDOWS_APPLICATION_ERROR_LOGS,
        WindowsSystemErrorLogs = HSM_DEFAULT_WINDOWS_SYSTEM_ERROR_LOGS,
        WindowsApplicationWarningLogs = HSM_DEFAULT_WINDOWS_APPLICATION_WARNING_LOGS,
        WindowsSystemWarningLogs = HSM_DEFAULT_WINDOWS_SYSTEM_WARNING_LOGS,

        NetworkConnectionsEstablished = HSM_DEFAULT_NETWORK_CONNECTIONS_ESTABLISHED,
        NetworkConnectionFailures = HSM_DEFAULT_NETWORK_CONNECTION_FAILURES,
        NetworkConnectionsReset = HSM_DEFAULT_NETWORK_CONNECTIONS_RESET,

        CollectorAlive = HSM_DEFAULT_COLLECTOR_ALIVE,
        CollectorVersion = HSM_DEFAULT_COLLECTOR_VERSION,
        CollectorErrors = HSM_DEFAULT_COLLECTOR_ERRORS,
        ProductVersion = HSM_DEFAULT_PRODUCT_VERSION,
        ServiceStatus = HSM_DEFAULT_SERVICE_STATUS,

        QueueOverflow = HSM_DEFAULT_QUEUE_OVERFLOW,
        QueuePackageValuesCount = HSM_DEFAULT_QUEUE_PACKAGE_VALUES_COUNT,
        QueuePackageProcessTime = HSM_DEFAULT_QUEUE_PACKAGE_PROCESS_TIME,
        QueuePackageContentSize = HSM_DEFAULT_QUEUE_PACKAGE_CONTENT_SIZE,
    };

    /// Deterministic substitutions for the volatile path/alert segments the managed prototypes read
    /// from the live machine. Unset fields take the documented default (process => "process",
    /// disk => "C"). `service_name`, `is_host_service`, and `product_version` are RESERVED for the
    /// live-value follow-up and currently do not affect the registration payload.
    struct DefaultSensorParams
    {
        std::optional<std::string> process_name;
        std::optional<std::string> disk_letter;
        std::optional<std::string> service_name;
        bool is_host_service = false;
        std::optional<std::string> product_version;

        /// Lower to the C struct. The result borrows this object's string storage, so the
        /// DefaultSensorParams must outlive the AddDefaultSensor call (it always does — consumed inline).
        hsm_default_sensor_params_t ToNative() const
        {
            hsm_default_sensor_params_t native = hsm_default_sensor_params_default();

            if (process_name.has_value())
                native.process_name = process_name->c_str();
            if (disk_letter.has_value())
                native.disk_letter = disk_letter->c_str();
            if (service_name.has_value())
                native.service_name = service_name->c_str();
            native.is_host_service = is_host_service ? 1 : 0;
            if (product_version.has_value())
                native.product_version = product_version->c_str();

            return native;
        }
    };
} // namespace hsm::collector
