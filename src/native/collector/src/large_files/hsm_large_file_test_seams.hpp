#pragma once

/// @file
/// @brief Test-only seams of large files (#1887), for the native tests and the conformance driver. Not part
/// of the public headers; defined in hsm_collector.cpp.

#include "hsm_collector/hsm_collector.h"

#include <cstdint>
#include <functional>
#include <string>

namespace hsm::collector::large_files
{
    class LargeFiles;
    struct FileConnectionOptions;
} // namespace hsm::collector::large_files

namespace hsm::collector::large_files::test
{
    /// The collector's large files, made when absent (null after dispose).
    LargeFiles* Of(hsm_collector_t* collector);
    /// The next loop of the uploader scans every watch now.
    void ScanNow(hsm_collector_t* collector);
    /// Called on the uploader thread for every wait it begins: what, and how long in ms.
    void OnWait(hsm_collector_t* collector, std::function<void(const std::string& what, std::int64_t ms)> hook);
    /// Adjusts the file connection's options of the runs started after the call (in the large files build).
    void AdjustConnection(hsm_collector_t* collector, std::function<void(FileConnectionOptions&)> adjust);
    bool UploaderAlive(hsm_collector_t* collector);
    /// Called on the uploader thread when a file was stored ("stored", then "deleted"; LargeFiles::on_finish_for_tests).
    void OnFinish(hsm_collector_t* collector, std::function<void(const std::string& phase)> hook);
    /// File values given a route copy (their content kept beside their JSON) since the collector was made.
    std::uint64_t RouteCopies(hsm_collector_t* collector);
    /// Routed sends waiting for the file connection (LargeFiles::RoutedWaitingForTests); 0 without large files.
    std::size_t RoutedWaiting(hsm_collector_t* collector);
} // namespace hsm::collector::large_files::test
