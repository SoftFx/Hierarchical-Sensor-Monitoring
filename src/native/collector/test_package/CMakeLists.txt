cmake_minimum_required(VERSION 3.21)

project(hsm_consumer LANGUAGES CXX)

find_package(hsm_collector CONFIG REQUIRED)

add_executable(hsm_consumer src/consumer.cpp)
target_link_libraries(hsm_consumer PRIVATE hsm_collector::hsm_collector_cpp)
target_compile_features(hsm_consumer PRIVATE cxx_std_17)
