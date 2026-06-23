# Server-monitor example (#1165): streams custom sensors to a real HSM server over the live HTTP
# transport. Built only with HSM_COLLECTOR_HTTP=ON (links libcurl via hsm_collector_cpp -> core).
# Warning/sanitizer helpers come from the parent CMakeLists (CMake functions are global).
add_executable(hsm_server_monitor main.cpp)
target_link_libraries(hsm_server_monitor PRIVATE hsm_collector_cpp)
target_compile_features(hsm_server_monitor PRIVATE cxx_std_17)
hsm_collector_set_warnings(hsm_server_monitor)
hsm_collector_apply_sanitizer(hsm_server_monitor)
