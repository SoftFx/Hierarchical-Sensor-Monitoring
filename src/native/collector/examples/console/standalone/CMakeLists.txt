# Standalone consumer (#1100): the clean-machine proof that the installed package is usable purely
# through find_package(hsm_collector) — no access to the collector source tree beyond this directory.
# The CI install-consume lane installs the collector to a prefix, then configures + builds this
# project with CMAKE_PREFIX_PATH pointing at that prefix. It reuses the in-tree example source.
cmake_minimum_required(VERSION 3.20)

project(hsm_console_standalone LANGUAGES CXX)

find_package(hsm_collector CONFIG REQUIRED)

add_executable(hsm_console_standalone ../main.cpp)
target_link_libraries(hsm_console_standalone PRIVATE hsm_collector::hsm_collector_cpp)
target_compile_features(hsm_console_standalone PRIVATE cxx_std_17)
