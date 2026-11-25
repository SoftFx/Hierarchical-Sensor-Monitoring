#pragma once

/// @file
/// @brief Test-only seams of the table pipeline (#1848). Not part of the public headers and not exported
/// by the legacy wrapper (its export list comes from hsm_collector.h): the test binary links the static
/// core and reaches them by C++ name.

#include "hsm_collector/hsm_collector.h"

#include "hsm_table_channel.hpp"

#include <functional>
#include <memory>

namespace hsm::collector::tables
{
    class TablePipeline;
    class TableSensor;
    class SessionTableChannel;
    struct SessionTableChannelOptions;

    namespace test
    {
        /// Installs the channel every table sensor of the collector uses from its next Start (and at its
        /// creation). Call before the first table sensor.
        void InstallChannel(hsm_collector_t* collector, std::shared_ptr<ITableChannel> channel);

        /// No tick task and no delivery thread: RunTick does a tick with its registration and its
        /// delivery on the caller's thread, and a stop drains on the caller's thread. Call before the
        /// first table sensor.
        void SetManualTick(hsm_collector_t* collector, bool manual);

        /// One table tick on the caller's thread. Nothing when the collector has no table sensor.
        void RunTick(hsm_collector_t* collector);

        /// The collector's pipeline; null while it has no table sensor.
        TablePipeline* Pipeline(hsm_collector_t* collector);

        /// The sensor behind a handle.
        TableSensor* Sensor(hsm_table_sensor_t* sensor);

        /// Runs inside hsm_collector_create_table_sensor after its first check of the collector's state
        /// and before it takes the lock of the table state (a test disposes the collector there).
        void SetBeforeCreateLock(hsm_collector_t* collector, std::function<void()> hook);

        /// With a manual tick: what the delivery thread does when its channel asks for it (opens the
        /// table connection, reconnects, pings), on the caller's thread.
        void ServiceChannelNow(hsm_collector_t* collector);

        /// The collector's session table channel, null until its first table sensor made one. Defined
        /// in builds with HSM_COLLECTOR_HTTP and HSM_COLLECTOR_TABLES only.
        std::shared_ptr<SessionTableChannel> SessionChannel(hsm_collector_t* collector);

        /// Adjusts the options of the session table channel the collector makes (shorter pauses, the
        /// test end of an ack wait). Before the first table sensor; HTTP and tables builds only.
        void SetSessionChannelOptions(hsm_collector_t* collector, std::function<void(SessionTableChannelOptions&)> adjust);
    } // namespace test
} // namespace hsm::collector::tables
