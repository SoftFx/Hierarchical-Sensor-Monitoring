# In-tree console example (#1100). Built as part of the main tree when HSM_COLLECTOR_BUILD_EXAMPLES
# is ON; the warning/sanitizer helpers come from the parent CMakeLists (CMake functions are global).
# The clean-machine find_package proof lives in ./standalone (built by the CI install-consume lane).
add_executable(hsm_console_example main.cpp)
target_link_libraries(hsm_console_example PRIVATE hsm_collector_cpp)
target_compile_features(hsm_console_example PRIVATE cxx_std_17)
hsm_collector_set_warnings(hsm_console_example)
hsm_collector_apply_sanitizer(hsm_console_example)
