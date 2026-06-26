# Windows live-monitor example (#1164): standard default sensors with live PDH/Win32 values streamed
# to a real server. Windows + HTTP only (UseHttpTransport + InstallWindowsMetricSources).
add_executable(hsm_windows_monitor main.cpp)
target_link_libraries(hsm_windows_monitor PRIVATE hsm_collector_cpp)
target_compile_features(hsm_windows_monitor PRIVATE cxx_std_17)
hsm_collector_set_warnings(hsm_windows_monitor)
hsm_collector_apply_sanitizer(hsm_windows_monitor)
