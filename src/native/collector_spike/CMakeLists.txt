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
add_conformance_test(conformance_value_int_contract value_int_contract.hsmtest)
add_conformance_test(conformance_cardinality_int_contract cardinality_int_contract.hsmtest)
add_conformance_test(conformance_instant_mixed_contract instant_mixed_contract.hsmtest)
add_conformance_test(conformance_last_value_contract last_value_contract.hsmtest)
