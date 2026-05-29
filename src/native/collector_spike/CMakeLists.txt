cmake_minimum_required(VERSION 3.20)

project(HsmCollectorNativeSpike LANGUAGES CXX)

add_library(hsm_collector_spike STATIC
    src/hsm_collector.cpp
)

target_include_directories(hsm_collector_spike
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_features(hsm_collector_spike PUBLIC cxx_std_17)

if(MSVC)
    target_compile_options(hsm_collector_spike PRIVATE /W4 /permissive-)
else()
    target_compile_options(hsm_collector_spike PRIVATE -Wall -Wextra -Wpedantic)
endif()

add_executable(hsm_collector_spike_tests
    tests/hsm_collector_spike_tests.cpp
)

target_link_libraries(hsm_collector_spike_tests PRIVATE hsm_collector_spike)

enable_testing()

function(add_spike_test name)
    add_test(NAME ${name} COMMAND hsm_collector_spike_tests ${name})
endfunction()

add_spike_test(c_abi_before_start_drops_value)
add_spike_test(c_abi_running_collector_stores_int_payload)
add_spike_test(c_abi_duplicate_sensor_path_is_idempotent)
add_spike_test(c_abi_long_comment_is_trimmed)
add_spike_test(c_abi_invalid_arguments_return_errors)
add_spike_test(c_abi_missing_payload_returns_not_found)
add_spike_test(cpp_wrapper_creates_sensor_and_reads_payload)
add_spike_test(cpp_wrapper_reports_invalid_path)
add_spike_test(cpp_wrapper_start_twice_reports_invalid_state)

add_test(
    NAME conformance_instant_int_contract
    COMMAND hsm_collector_spike_tests conformance_instant_int_contract
            ${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/conformance/collector/instant_int_contract.hsmtest
)
