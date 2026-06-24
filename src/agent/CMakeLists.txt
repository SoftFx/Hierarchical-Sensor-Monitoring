# HSM Agent — an always-on Windows service that hosts the native collector and streams this
# computer's metrics to an HSM server (epic #1167). It links the in-tree collector library.
#
# Target topology:
#   hsm_agent_config  STATIC  the platform-agnostic config.json parser. Built on EVERY platform so
#                             the parser unit tests run in any CI lane (no collector / Win32 dep).
#   hsm_agent_tests   EXE     name-dispatched config unit tests.
#   hsm-agent         EXE     the Windows service executable (WIN32 only). Pulls the collector with
#                             the libcurl HTTP transport (HSM_COLLECTOR_HTTP).
cmake_minimum_required(VERSION 3.21)

# Self-contained Windows agent: the downloadable bundle ships a SINGLE hsm-agent.exe with no
# redistributable DLLs, so build the curl HTTP transport against the STATIC vcpkg triplet
# (static curl[ssl]=Schannel + zlib, no libcurl/zlib DLLs) and link the CRT statically below.
# Set the triplet before project() so the vcpkg toolchain (included during project()) picks it up.
# Gate on the host (the Win32 service only builds on Windows) and allow an explicit override.
if(CMAKE_HOST_WIN32 AND NOT DEFINED VCPKG_TARGET_TRIPLET)
    set(VCPKG_TARGET_TRIPLET "x64-windows-static" CACHE STRING "vcpkg triplet for the self-contained agent build")
endif()

project(HsmAgent VERSION 0.5.0 LANGUAGES CXX)

if(MSVC)
    # Static CRT (/MT) to match the static triplet — the exe must run on a clean machine with no VC++
    # redistributable. Applies to the agent, the linked collector subdir, and the portable config lib.
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

option(HSM_AGENT_WERROR "Treat compiler warnings as errors" OFF)
option(HSM_AGENT_BUILD_TESTS "Build the agent unit tests" ON)

function(hsm_agent_set_warnings target)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive-)
        if(HSM_AGENT_WERROR)
            target_compile_options(${target} PRIVATE /WX)
        endif()
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
        if(HSM_AGENT_WERROR)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()
endfunction()

# --- Portable config layer + tests (every platform) ----------------------------------------------
# cpu_top.cpp moved to the native collector (#1179): SelectTopN + WindowsCpuSampler now live in
# src/native/collector/src/cpu_top.cpp and are exposed via hsm_collector_enable_top_cpu_sensors.
add_library(hsm_agent_config STATIC src/config.cpp)
target_include_directories(hsm_agent_config PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_compile_features(hsm_agent_config PUBLIC cxx_std_17)
hsm_agent_set_warnings(hsm_agent_config)

if(HSM_AGENT_BUILD_TESTS)
    enable_testing()
    add_executable(hsm_agent_tests tests/agent_tests.cpp)
    target_link_libraries(hsm_agent_tests PRIVATE hsm_agent_config)
    hsm_agent_set_warnings(hsm_agent_tests)

    foreach(name IN ITEMS
            agent_config_minimal_applies_defaults
            agent_config_full_maps_every_field
            agent_config_rejects_blank_access_key
            agent_config_rejects_missing_address
            agent_config_rejects_bad_port
            agent_config_rejects_malformed_json
            agent_config_rejects_wrong_typed_field
            agent_config_ignores_unknown_fields
            agent_config_decodes_string_escapes
            agent_topcpu_config_parses_and_defaults
            agent_topcpu_rejects_bad_config)
        add_test(NAME ${name} COMMAND hsm_agent_tests ${name})
    endforeach()
endif()

# --- Windows service executable ------------------------------------------------------------------
if(WIN32)
    # The agent must reach a real server, so the collector is built with the libcurl HTTP transport.
    set(HSM_COLLECTOR_HTTP ON CACHE BOOL "Build the libcurl HTTP transport" FORCE)
    add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../native/collector ${CMAKE_CURRENT_BINARY_DIR}/collector)

    # Regenerate build_timestamp.cpp on every build so --version always shows the real build time.
    # Using add_custom_target(ALL) ensures the cmake script runs each time; writing a new file
    # (timestamp always changes) forces recompilation of that one translation unit.
    set(TIMESTAMP_CPP "${CMAKE_CURRENT_BINARY_DIR}/build_timestamp.cpp")
    add_custom_target(hsm_agent_timestamp ALL
        COMMAND ${CMAKE_COMMAND}
            -DOUTPUT=${TIMESTAMP_CPP}
            -P ${CMAKE_CURRENT_SOURCE_DIR}/cmake/gen_timestamp.cmake
        COMMENT "Generating build timestamp"
        VERBATIM
    )
    # Create a placeholder so cmake doesn't error on the missing source before the first build.
    if(NOT EXISTS "${TIMESTAMP_CPP}")
        file(WRITE "${TIMESTAMP_CPP}" "namespace hsm { namespace agent { const char* BuildTimestamp() { return \"(unknown)\"; } } }\n")
    endif()
    set_source_files_properties("${TIMESTAMP_CPP}" PROPERTIES GENERATED TRUE)

    add_executable(hsm-agent
        src/main.cpp
        src/agent_runtime.cpp
        src/paths.cpp
        src/logging/file_logger.cpp
        src/logging/agent_logger.cpp
        src/service/event_log.cpp
        src/service/service_install.cpp
        src/service/windows_service.cpp
        "${TIMESTAMP_CPP}"
    )
    add_dependencies(hsm-agent hsm_agent_timestamp)
    target_link_libraries(hsm-agent PRIVATE hsm_agent_config hsm_collector_cpp advapi32)
    target_compile_features(hsm-agent PRIVATE cxx_std_17)
    target_compile_definitions(hsm-agent PRIVATE HSM_AGENT_VERSION="${PROJECT_VERSION}")
    hsm_agent_set_warnings(hsm-agent)

    # After every build, copy the exe into packaging/ so that folder stays a ready-to-run bundle.
    add_custom_command(TARGET hsm-agent POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            "$<TARGET_FILE:hsm-agent>"
            "${CMAKE_CURRENT_SOURCE_DIR}/packaging/hsm-agent.exe"
        COMMENT "Copying hsm-agent.exe -> src/agent/packaging/"
    )
endif()
