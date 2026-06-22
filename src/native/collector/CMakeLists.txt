# 3.21 (matches CMakePresets.json) so PROJECT_IS_TOP_LEVEL is always defined — it gates the
# install/example defaults below and is only auto-set from 3.21.
cmake_minimum_required(VERSION 3.21)

# VERSION tracks the C ABI semver (HSM_COLLECTOR_VERSION_* in include/hsm_collector/hsm_collector.h);
# keep the two in sync. It feeds the find_package(hsm_collector x.y) version check (#1100).
project(HsmCollectorNative VERSION 0.4.0 LANGUAGES CXX)

# ---------------------------------------------------------------------------
# Target topology (epic #1093 / issue #1095 §1 — "core lib / C ABI lib / C++
# wrapper / tests"):
#
#   hsm_collector_core  STATIC    the implementation AND the extern "C" ABI
#                                 symbols. The internal C++ core and the C
#                                 boundary currently share one translation
#                                 unit; splitting them into separate compiled
#                                 libraries waits until the ABI surface settles
#                                 across the rest of the #1095 wave (state
#                                 machine, options struct, logging) so we do
#                                 not edit both halves of a split five times.
#                                 Tracked in the spike journal.
#   hsm_collector_cpp   INTERFACE the header-only C++ wrapper (hsm_collector.hpp)
#                                 over the C ABI; links the core.
#   hsm_collector_tests EXE       the cross-language conformance driver plus the
#                                 native C-ABI / validation regression tests.
# ---------------------------------------------------------------------------

# std::thread / std::condition_variable need an explicit pthread link on POSIX
# toolchains (gcc without -pthread links but may crash at runtime).
find_package(Threads REQUIRED)

# Standard install layout (CMAKE_INSTALL_INCLUDEDIR/LIBDIR/...). Included early because the
# target include dirs below reference CMAKE_INSTALL_INCLUDEDIR for their $<INSTALL_INTERFACE:>.
include(GNUInstallDirs)

# Whether to emit install()/find_package() rules (#1100). On by default only for a top-level build
# so an add_subdirectory() consumer is not forced to install the collector. PROJECT_IS_TOP_LEVEL is
# guaranteed defined by the 3.21 minimum above.
set(_hsm_default_install ${PROJECT_IS_TOP_LEVEL})
option(HSM_COLLECTOR_INSTALL "Generate install + find_package package for the native collector" ${_hsm_default_install})

# Build the in-tree console example (#1100). Defaults to the install setting.
option(HSM_COLLECTOR_BUILD_EXAMPLES "Build the native collector example apps" ${_hsm_default_install})

# Build the test/conformance binary (#1100). Off for package builds (Conan/vcpkg) and
# add_subdirectory consumers, which want only the library.
option(HSM_COLLECTOR_BUILD_TESTS "Build the native collector test + conformance binary" ${_hsm_default_install})

# Build the micro-benchmark (#1101). OFF by default — only the scheduled
# native-collector-benchmark lane turns it on; it is not part of the normal
# build/test matrix.
option(HSM_COLLECTOR_BUILD_BENCH "Build the native collector enqueue-throughput benchmark" OFF)

# Treat warnings as errors (off by default so local dev is not blocked; CI
# turns it on). The corpus + unit tests must build warning-clean under it.
option(HSM_COLLECTOR_WERROR "Treat compiler warnings as errors" OFF)

# Build with a sanitizer: "address" (ASan+UBSan) or "thread" (TSan). Empty =
# none. Driven by the CMakePresets asan/tsan presets and the sanitized CI lane.
# MSVC supports only address; clang/gcc support both.
set(HSM_COLLECTOR_SANITIZER "" CACHE STRING "Sanitizer: address, thread, or empty")
set_property(CACHE HSM_COLLECTOR_SANITIZER PROPERTY STRINGS "" "address" "thread")

# Warning flags shared by every first-party target we compile.
function(hsm_collector_set_warnings target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive-)
        if(HSM_COLLECTOR_WERROR)
            target_compile_options(${target} PRIVATE /WX)
        endif()
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
        if(HSM_COLLECTOR_WERROR)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()
endfunction()

# Apply the selected sanitizer to a first-party target (compile + link).
function(hsm_collector_apply_sanitizer target)
    if(HSM_COLLECTOR_SANITIZER STREQUAL "")
        return()
    endif()

    if(MSVC)
        if(NOT HSM_COLLECTOR_SANITIZER STREQUAL "address")
            message(FATAL_ERROR "MSVC supports only the address sanitizer (got '${HSM_COLLECTOR_SANITIZER}')")
        endif()
        target_compile_options(${target} PRIVATE /fsanitize=address)
    else()
        if(HSM_COLLECTOR_SANITIZER STREQUAL "address")
            set(flags -fsanitize=address,undefined -fno-sanitize-recover=all)
        elseif(HSM_COLLECTOR_SANITIZER STREQUAL "thread")
            set(flags -fsanitize=thread)
        else()
            message(FATAL_ERROR "Unknown HSM_COLLECTOR_SANITIZER '${HSM_COLLECTOR_SANITIZER}' (use address or thread)")
        endif()
        target_compile_options(${target} PRIVATE ${flags} -fno-omit-frame-pointer -g)
        target_link_options(${target} PRIVATE ${flags})
    endif()
endfunction()

add_library(hsm_collector_core STATIC
    src/hsm_collector.cpp
    src/platform/hsm_windows_metric_sources.cpp  # self-guarded #if _WIN32 (#1164)
)

# Windows live metric readers (#1164) link PDH (perf counters) + Win32.
if(WIN32)
    target_link_libraries(hsm_collector_core PRIVATE pdh)
endif()

target_include_directories(hsm_collector_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>
)

target_compile_features(hsm_collector_core PUBLIC cxx_std_17)

target_link_libraries(hsm_collector_core PUBLIC Threads::Threads)

hsm_collector_set_warnings(hsm_collector_core)
hsm_collector_apply_sanitizer(hsm_collector_core)

# libcurl HTTP transport (#1096 §12). Opt-in so the serialization/behavior build and the existing
# CI lanes need no curl/vcpkg; the dedicated HTTP lane sets it ON (vcpkg.json provides curl[ssl] —
# Schannel on Windows, OpenSSL elsewhere). curl headers are SYSTEM so the -Werror gate ignores
# them; only hsm_http_transport.cpp includes <curl/curl.h>.
option(HSM_COLLECTOR_HTTP "Build the libcurl HTTP transport" OFF)
if(HSM_COLLECTOR_HTTP)
    find_package(CURL REQUIRED)
    target_sources(hsm_collector_core PRIVATE src/hsm_http_transport.cpp)
    target_compile_definitions(hsm_collector_core PRIVATE HSM_COLLECTOR_HTTP=1)
    target_link_libraries(hsm_collector_core PRIVATE CURL::libcurl)
    target_include_directories(hsm_collector_core SYSTEM PRIVATE ${CURL_INCLUDE_DIRS})
endif()

# Header-only C++ RAII wrapper over the C ABI.
add_library(hsm_collector_cpp INTERFACE)

target_include_directories(hsm_collector_cpp
    INTERFACE
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>
)

target_link_libraries(hsm_collector_cpp INTERFACE hsm_collector_core)

target_compile_features(hsm_collector_cpp INTERFACE cxx_std_17)

# ---------------------------------------------------------------------------
# Packaging (#1100): CMake install + find_package(hsm_collector) consumer
# config (the baseline that satisfies native consumers and the standalone
# example's clean-build proof). The Conan recipe (conanfile.py) and the vcpkg
# overlay port (vcpkg-port/) build on top of these install rules. NuGet native
# assets were intentionally dropped (no mixed .NET/native consumer).
# ---------------------------------------------------------------------------
if(HSM_COLLECTOR_INSTALL)
    include(CMakePackageConfigHelpers)

    install(TARGETS hsm_collector_core hsm_collector_cpp
        EXPORT hsm_collectorTargets
        ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
        LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
        RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    )

    install(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}/include/hsm_collector
        DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
    )

    set(HSM_COLLECTOR_INSTALL_CMAKEDIR ${CMAKE_INSTALL_LIBDIR}/cmake/hsm_collector)

    install(EXPORT hsm_collectorTargets
        FILE hsm_collectorTargets.cmake
        NAMESPACE hsm_collector::
        DESTINATION ${HSM_COLLECTOR_INSTALL_CMAKEDIR}
    )

    configure_package_config_file(
        ${CMAKE_CURRENT_SOURCE_DIR}/cmake/hsm_collectorConfig.cmake.in
        ${CMAKE_CURRENT_BINARY_DIR}/hsm_collectorConfig.cmake
        INSTALL_DESTINATION ${HSM_COLLECTOR_INSTALL_CMAKEDIR}
    )
    write_basic_package_version_file(
        ${CMAKE_CURRENT_BINARY_DIR}/hsm_collectorConfigVersion.cmake
        VERSION ${PROJECT_VERSION}
        COMPATIBILITY SameMajorVersion
    )
    install(FILES
        ${CMAKE_CURRENT_BINARY_DIR}/hsm_collectorConfig.cmake
        ${CMAKE_CURRENT_BINARY_DIR}/hsm_collectorConfigVersion.cmake
        DESTINATION ${HSM_COLLECTOR_INSTALL_CMAKEDIR}
    )
endif()

# In-tree console example. The standalone find_package consumer lives under
# examples/console/standalone and is built separately against the installed package (CI lane).
if(HSM_COLLECTOR_BUILD_EXAMPLES)
    add_subdirectory(examples/console)
    # The server-monitor example POSTs to a real server, so it needs the libcurl transport.
    if(HSM_COLLECTOR_HTTP)
        add_subdirectory(examples/server-monitor)
        # The crypto-monitor value-source plugin example fetches quotes via libcurl.
        add_subdirectory(examples/crypto-monitor)
        # The windows-monitor example uses the Windows PDH live readers — Windows only.
        if(WIN32)
            add_subdirectory(examples/windows-monitor)
        endif()
    endif()
endif()

# Enqueue-throughput micro-benchmark (#1101). Gated OFF; the scheduled benchmark lane builds it.
if(HSM_COLLECTOR_BUILD_BENCH)
    add_executable(hsm_collector_bench bench/hsm_collector_bench.cpp)
    target_link_libraries(hsm_collector_bench PRIVATE hsm_collector_cpp)
    hsm_collector_set_warnings(hsm_collector_bench)
    hsm_collector_apply_sanitizer(hsm_collector_bench)
    if(WIN32)
        target_link_libraries(hsm_collector_bench PRIVATE psapi)
    endif()
endif()

# The test/conformance binary and its ctest registrations (gated so package/consumer builds skip
# them). The body below is intentionally left un-indented to keep the guard a minimal diff.
if(HSM_COLLECTOR_BUILD_TESTS)

add_executable(hsm_collector_tests
    tests/hsm_collector_tests.cpp
)

target_link_libraries(hsm_collector_tests PRIVATE hsm_collector_core)

hsm_collector_set_warnings(hsm_collector_tests)
hsm_collector_apply_sanitizer(hsm_collector_tests)

# The HTTP-transport E2E test compiles only in the HTTP build (it needs the transport + the
# in-proc capture server). The define gates the #ifdef'd test code; ws2_32 auto-links on MSVC.
if(HSM_COLLECTOR_HTTP)
    target_compile_definitions(hsm_collector_tests PRIVATE HSM_COLLECTOR_HTTP=1)
endif()

enable_testing()

if(HSM_COLLECTOR_HTTP)
    add_test(NAME native_http_transport_posts_to_capture_server
        COMMAND hsm_collector_tests native_http_transport_posts_to_capture_server
    )
    add_test(NAME native_http_live_send_posts_to_capture_server
        COMMAND hsm_collector_tests native_http_live_send_posts_to_capture_server
    )
    add_test(NAME native_http_registers_sensors_on_start
        COMMAND hsm_collector_tests native_http_registers_sensors_on_start
    )
endif()

add_test(NAME native_invalid_argument_clears_out_params
    COMMAND hsm_collector_tests native_invalid_argument_clears_out_params
)
add_test(NAME native_add_after_collector_destroy_is_rejected
    COMMAND hsm_collector_tests native_add_after_collector_destroy_is_rejected
)
add_test(NAME native_sent_json_failure_reports_fresh_error
    COMMAND hsm_collector_tests native_sent_json_failure_reports_fresh_error
)
add_test(NAME native_wrapper_sent_json_missing_throws_message
    COMMAND hsm_collector_tests native_wrapper_sent_json_missing_throws_message
)
add_test(NAME native_wrapper_registration_matches_abi
    COMMAND hsm_collector_tests native_wrapper_registration_matches_abi
)
add_test(NAME native_wrapper_alert_builder_matches_abi
    COMMAND hsm_collector_tests native_wrapper_alert_builder_matches_abi
)
add_test(NAME native_wrapper_value_path_posts_through
    COMMAND hsm_collector_tests native_wrapper_value_path_posts_through
)
add_test(NAME native_wrapper_lifetime_move_semantics
    COMMAND hsm_collector_tests native_wrapper_lifetime_move_semantics
)
add_test(NAME native_wrapper_callbacks_bridge_std_function
    COMMAND hsm_collector_tests native_wrapper_callbacks_bridge_std_function
)

set(NATIVE_REGRESSION_TESTS
    native_create_rejects_null_server_address
    native_create_rejects_blank_server_address
    native_create_rejects_null_access_key
    native_create_rejects_blank_access_key
    native_slash_only_module_is_omitted_from_payload_path
    native_slash_only_computer_name_is_omitted_from_payload_path
    native_slash_only_module_and_computer_name_are_omitted_from_payload_path
    native_whitespace_only_path_is_rejected
    native_instant_string_null_value_is_invalid_and_not_sent
    native_last_value_string_null_default_is_invalid
    native_last_value_string_null_update_is_invalid_and_preserves_previous
    native_json_escapes_control_chars_in_string_value
    native_json_escapes_control_chars_in_comment
    native_json_escapes_control_chars_in_path
    native_json_escapes_control_chars_in_options_path_prefix
    native_double_nan_is_rejected_and_not_sent
    native_double_positive_infinity_is_rejected_and_not_sent
    native_double_negative_infinity_is_rejected_and_not_sent
    native_invalid_status_on_instant_value_is_rejected_and_not_sent
    native_invalid_status_on_last_value_preserves_previous_snapshot
    native_version_matches_macro
    native_status_tracks_lifecycle
    native_dispose_is_terminal_and_idempotent
    native_dispose_from_running_stops
    native_lifecycle_listener_receives_transitions
    native_lifecycle_listener_exception_is_isolated
    native_wire_iso_from_unix_ms_matches_net
    native_wire_value_json_matches_net_byte_layout
    native_wire_bar_json_matches_net_byte_layout
    native_wire_file_json_matches_net_byte_layout
    native_wire_timespan_and_version_match_net
    native_wire_registration_json_matches_net_byte_layout
    native_wire_registration_with_alerts_matches_net_byte_layout
    native_wire_registration_full_options_matches_net_byte_layout
    native_version_string_matches_net
    native_alert_scheduled_notification_matches_net
    native_default_sensor_wire_matches_net
    native_default_sensor_group_composition
    native_metric_source_seam_lifecycle
    native_metric_source_drives_default_bar_sensor
    native_metric_source_drives_custom_double_sensor
    # native_windows_metric_sources_produce_live_value is registered below (Windows only).
    native_prototype_merge_pins_identity_overrides_metadata
    native_http_endpoint_routing_matches_net
    native_http_retry_policy_matches_net
    native_lifecycle_listener_can_register_another_listener
    native_test_connection_reports_reachable
    native_max_sensors_cap_rejects_beyond_limit
    native_create_rejects_negative_option_fields
    native_logger_sink_can_be_set_and_cleared
    native_scheduler_clock_seam_drives_periodic_posts
    native_scheduler_on_error_isolates_throwing_callback
    native_logger_deduplicates_repeated_errors_within_window
    native_logger_zero_window_logs_every_error
    native_retry_meeting_full_queue_is_dropped_not_evicting_queued_values
    native_retry_below_capacity_is_always_redelivered
)

foreach(name IN LISTS NATIVE_REGRESSION_TESTS)
    add_test(NAME ${name}
        COMMAND hsm_collector_tests ${name}
    )
endforeach()

# Windows-only smoke: the real PDH metric-source factory drives a live Total CPU reading (#1164).
if(WIN32)
    add_test(NAME native_windows_metric_sources_produce_live_value
        COMMAND hsm_collector_tests native_windows_metric_sources_produce_live_value
    )
endif()

function(add_conformance_test name fixture)
    add_test(
        NAME ${name}
        COMMAND hsm_collector_tests ${name}
                ${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/conformance/collector/${fixture}
    )
endfunction()

add_conformance_test(conformance_instant_int_contract instant_int_contract.hsmtest)
add_conformance_test(conformance_lifecycle_int_contract lifecycle_int_contract.hsmtest)
add_conformance_test(conformance_stress_int_contract stress_int_contract.hsmtest)
add_conformance_test(conformance_stress_mixed_contract stress_mixed_contract.hsmtest)
add_conformance_test(conformance_value_int_contract value_int_contract.hsmtest)
add_conformance_test(conformance_cardinality_int_contract cardinality_int_contract.hsmtest)
add_conformance_test(conformance_instant_mixed_contract instant_mixed_contract.hsmtest)
add_conformance_test(conformance_last_value_contract last_value_contract.hsmtest)
add_conformance_test(conformance_enum_contract enum_contract.hsmtest)
add_conformance_test(conformance_bar_int_contract bar_int_contract.hsmtest)
add_conformance_test(conformance_bar_double_contract bar_double_contract.hsmtest)
add_conformance_test(conformance_bar_partial_contract bar_partial_contract.hsmtest)
add_conformance_test(conformance_bar_rollover_contract bar_rollover_contract.hsmtest)
add_conformance_test(conformance_queue_overflow_contract queue_overflow_contract.hsmtest)
add_conformance_test(conformance_sender_retry_contract sender_retry_contract.hsmtest)
add_conformance_test(conformance_flush_contract flush_contract.hsmtest)
add_conformance_test(conformance_rate_contract rate_contract.hsmtest)
add_conformance_test(conformance_function_contract function_contract.hsmtest)
add_conformance_test(conformance_file_contract file_contract.hsmtest)
add_conformance_test(conformance_number_format_contract number_format_contract.hsmtest)
add_conformance_test(conformance_registration_contract registration_contract.hsmtest)
add_conformance_test(conformance_timespan_version_contract timespan_version_contract.hsmtest)
add_conformance_test(conformance_alert_registration_contract alert_registration_contract.hsmtest)
add_conformance_test(conformance_options_surface_contract options_surface_contract.hsmtest)
add_conformance_test(conformance_service_commands_contract service_commands_contract.hsmtest)
add_conformance_test(conformance_default_sensors_contract default_sensors_contract.hsmtest)

add_test(
    NAME native_default_sensors_wire_golden
    COMMAND hsm_collector_tests native_default_sensors_wire_golden
            ${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/conformance/collector/golden/default_sensors_wire.golden
)

# Meta-suite ("test the tests"): must-FAIL fixtures with deliberately wrong
# expectations; the meta_must_fail runner passes only when the driver detects
# the failure. See tests/conformance/collector/meta/README.md.
function(add_meta_conformance_test fixture)
    get_filename_component(name ${fixture} NAME_WE)
    add_test(
        NAME ${name}
        COMMAND hsm_collector_tests meta_must_fail
                ${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/conformance/collector/meta/${fixture}
    )
endfunction()

add_meta_conformance_test(meta_bar_field_wrong_mean.hsmtest)
add_meta_conformance_test(meta_each_value_once_missing.hsmtest)
add_meta_conformance_test(meta_eventually_value_above_unreachable.hsmtest)
add_meta_conformance_test(meta_no_new_payloads_but_arriving.hsmtest)
add_meta_conformance_test(meta_payload_contains_wrong_value.hsmtest)
add_meta_conformance_test(meta_payload_not_contains_present.hsmtest)
add_meta_conformance_test(meta_sent_count_overcount.hsmtest)
add_meta_conformance_test(meta_sent_count_zero_but_sent.hsmtest)
add_meta_conformance_test(meta_unknown_action_rejected.hsmtest)
add_meta_conformance_test(meta_value_sequence_wrong_start.hsmtest)

endif() # HSM_COLLECTOR_BUILD_TESTS
