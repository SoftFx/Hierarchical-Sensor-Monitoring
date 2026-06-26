#pragma once

/// @file
/// @brief Umbrella header for the public C++ RAII API (`namespace hsm::collector`).
///
/// Include this single header to get the full developer-facing surface — the Collector facade,
/// all RAII sensor types, the SensorOptions/BarOptions/RateOptions builders, the fluent
/// AlertBuilder, the default-sensor catalog, and the Error exception type. Every type is a thin,
/// header-only convenience layer over the stable C ABI in hsm_collector.h; the wrapper adds no new
/// wire behavior (its byte output is identical to the raw C ABI, pinned by the
/// native_wrapper_* unit tests). Errors surface as hsm::collector::Error exceptions.

#include "hsm_collector/alerts.hpp"
#include "hsm_collector/collector.hpp"
#include "hsm_collector/default_sensors.hpp"
#include "hsm_collector/detail/callbacks.hpp"
#include "hsm_collector/enums.hpp"
#include "hsm_collector/error.hpp"
#include "hsm_collector/options.hpp"
#include "hsm_collector/sensors.hpp"
