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
add_test(NAME hsm_collector_spike_tests COMMAND hsm_collector_spike_tests)
