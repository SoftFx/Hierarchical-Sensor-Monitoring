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

add_test(NAME native_invalid_argument_clears_out_params
    COMMAND hsm_collector_spike_tests native_invalid_argument_clears_out_params
)
add_test(NAME native_add_after_collector_destroy_is_rejected
    COMMAND hsm_collector_spike_tests native_add_after_collector_destroy_is_rejected
)
add_test(NAME native_sent_json_failure_reports_fresh_error
    COMMAND hsm_collector_spike_tests native_sent_json_failure_reports_fresh_error
)
add_test(NAME native_wrapper_sent_json_missing_throws_message
    COMMAND hsm_collector_spike_tests native_wrapper_sent_json_missing_throws_message
)

function(add_conformance_test name fixture)
    add_test(
        NAME ${name}
        COMMAND hsm_collector_spike_tests ${name}
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
