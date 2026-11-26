# Native HSMCppWrapper.dll
#
# Drop-in replacement for the C++/CLI HSMCppWrapper: same public headers (src/wrapper/include), same
# exported ABI, but the implementation runs over the native hsm::collector C++ API instead of the
# managed HSMDataCollector. Consumers (e.g. tt-aggregator2) relink against this DLL with no source
# changes and no longer drag in the CLR.

cmake_minimum_required(VERSION 3.16)

# The wrapper's own version: the version of the wrapper-v<semver> release cut from this source (see
# packaging/README.md, "Versioning"). Independent of the native collector's HSM_COLLECTOR_VERSION.
# 2.0.0: the default transport became the collector session (a server without sessions receives
# nothing from a host that does not call DataCollectorProxy::UseHttpTransport), a breaking change.
# 2.1.0: native collector 0.16.0 (large files, #1887): the .def exports its new functions, and a file
# send above a quarter of the session frame limit travels on the file connection.
#
# Dependencies come from vcpkg manifest mode (vcpkg.json next to this file, pinned by its
# builtin-baseline): curl with WebSocket for the session, zstd and zlib for table sensors.
project(HSMCppWrapper VERSION 2.1.0 LANGUAGES CXX)

if(NOT CMAKE_CXX_STANDARD)
    set(CMAKE_CXX_STANDARD 17)
endif()
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# The wrapper drives a real HSM server, so it needs the libcurl network transport (the collector
# session and HTTP both live behind HSM_COLLECTOR_HTTP). Default it on for the native collector
# subbuild; an explicit -DHSM_COLLECTOR_HTTP=OFF (e.g. for a compile-only check without CURL) still wins.
if(NOT DEFINED HSM_COLLECTOR_HTTP)
    set(HSM_COLLECTOR_HTTP ON CACHE BOOL "Build the libcurl HTTP transport (required by the wrapper at runtime)" FORCE)
endif()
# Table sensors (proxy.Native().CreateTableSensor) need the table block writer, which needs zstd and
# zlib (vcpkg.json). Default on; an explicit -DHSM_COLLECTOR_TABLES=OFF still wins, and then every
# table sensor is Unavailable.
if(NOT DEFINED HSM_COLLECTOR_TABLES)
    set(HSM_COLLECTOR_TABLES ON CACHE BOOL "Build table sensor support (needs zstd and zlib)" FORCE)
endif()

# The default transport is the collector session, which needs a libcurl 8.14.0 or newer with
# WebSocket (ws/wss). find_package(CURL) accepts any curl, and a wrapper built against one without
# WebSocket would configure, link and load, then send nothing at run time (1.x built the same way
# worked over HTTP). So such a build is refused here. It reads what curl's own CMake package states
# (CURL_SUPPORTS_WS/WSS and CURL_VERSION, as vcpkg's curl installs it; FindCURL looks for that
# package first); a curl that does not state it is refused as well.
if(HSM_COLLECTOR_HTTP)
    find_package(CURL REQUIRED)
    set(_hsm_curl_version "${CURL_VERSION}")
    if(NOT _hsm_curl_version)
        set(_hsm_curl_version "${CURL_VERSION_STRING}")
    endif()
    if(NOT CURL_SUPPORTS_WS OR NOT CURL_SUPPORTS_WSS OR NOT _hsm_curl_version OR _hsm_curl_version VERSION_LESS 8.14.0)
        message(FATAL_ERROR
            "HSMCppWrapper: the libcurl found (version '${_hsm_curl_version}', CURL_DIR '${CURL_DIR}') has no WebSocket "
            "(ws/wss) support or is older than 8.14.0, or its CMake package does not say. The wrapper's default "
            "transport, the collector session, cannot work with it. Build with the vcpkg manifest src/wrapper/vcpkg.json "
            "(curl[websockets] at its pinned baseline): see src/wrapper/packaging/README.md.")
    endif()
    message(STATUS "HSMCppWrapper: libcurl ${_hsm_curl_version} with WebSocket")
endif()

# Bring in the native collector (provides hsm_collector_cpp -> hsm_collector_core).
if(NOT TARGET hsm_collector_cpp)
    add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../native/collector ${CMAKE_BINARY_DIR}/hsm_collector)
endif()

# Re-export the native C ABI from the DLL so a consumer can call hsm::collector directly via
# DataCollectorProxy::Native() (incremental migration off the wrapper) without a second copy of the
# collector runtime. The C ABI lives in the statically-linked hsm_collector_core, so a module-
# definition file re-exports its extern-C functions; it combines with the wrapper's __declspec
# exports. The .def is generated from the frozen ABI header so it never drifts out of sync.
if(MSVC)
    set(_hsm_abi_header ${CMAKE_CURRENT_SOURCE_DIR}/../native/collector/include/hsm_collector/hsm_collector.h)
    file(STRINGS ${_hsm_abi_header} _hsm_abi_lines REGEX "hsm_[A-Za-z0-9_]+[ \t]*\\(")
    set(_hsm_def_body "EXPORTS\n")
    set(_hsm_seen "")
    foreach(_line IN LISTS _hsm_abi_lines)
        # Skip typedefs (callback function-pointer types like `(*hsm_metric_read_t)(...)`): the C ABI
        # convention is that types end in `_t` and functions do not, so only real functions get exported.
        if(_line MATCHES "typedef")
            continue()
        endif()
        if(_line MATCHES "(hsm_[A-Za-z0-9_]+)[ \t]*\\(")
            set(_fn ${CMAKE_MATCH_1})
            if(NOT _fn MATCHES "_t$" AND NOT _fn IN_LIST _hsm_seen)
                list(APPEND _hsm_seen ${_fn})
                string(APPEND _hsm_def_body "    ${_fn}\n")
            endif()
        endif()
    endforeach()
    set(_hsm_def ${CMAKE_CURRENT_BINARY_DIR}/hsm_abi_exports.def)
    file(WRITE ${_hsm_def} "${_hsm_def_body}")
endif()

# One wrapper DLL: the shipped HSMCppWrapper and, built from the very same sources, the test DLL of
# WrapperTransportTests below.
function(hsm_wrapper_add_dll target)
    add_library(${target} SHARED
        DataCollector.cpp
        DataCollectorImpl.cpp
        HSMSensor.cpp
        HSMBarSensor.cpp
        HSMRateSensor.cpp
        HSMLastValueSensor.cpp
        HSMNoParamsFuncSensor.cpp
        HSMParamsFuncSensor.cpp
        HSMSensorOptions.cpp)

    # Public headers (the unchanged consumer ABI) + impl headers in this directory.
    target_include_directories(${target}
        PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
        PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

    # pch.h force-defines HSMWRAPPER_API as __declspec(dllexport) for this build; nothing else to do.
    target_link_libraries(${target} PRIVATE hsm_collector_cpp)

    if(MSVC)
        target_compile_options(${target} PRIVATE /EHsc /W3)
        # Emit a PDB for the Release DLL too (the VS Release config omits debug info by default, so the
        # handoff bundle otherwise ships Release without symbols for crash analysis at tt-aggregator2).
        # /Zi generates the debug info; the linker /DEBUG writes the .pdb; /OPT:REF /OPT:ICF restore the
        # size/speed folding that /DEBUG turns off, so Release stays optimized but symbolizable. Exports/
        # ABI are unaffected, so the relink stays byte-for-byte identical.
        target_compile_options(${target} PRIVATE $<$<CONFIG:Release>:/Zi>)
        target_link_options(${target} PRIVATE $<$<CONFIG:Release>:/DEBUG /OPT:REF /OPT:ICF>)
        target_sources(${target} PRIVATE ${_hsm_def})
    endif()
endfunction()

hsm_wrapper_add_dll(HSMCppWrapper)

# Statically pull the collector core into the DLL. The vcpkg packages are shared (x64-windows), so
# libcurl, zlib and zstd ship next to the DLL: vcpkg's app-local deployment copies them into the
# build output, and packaging/pack.ps1 bundles whatever it finds there.

# In-process smoke that drives the public ABI exactly as a consumer would (no live server needed).
option(HSMWRAPPER_BUILD_SMOKE "Build the WrapperConsole smoke executable" ON)
if(HSMWRAPPER_BUILD_SMOKE)
    add_executable(WrapperConsole WrapperConsole/WrapperSmoke.cpp)
    # Wrapper public headers + the native collector headers (for the Native()/variant-A path). Note it
    # links ONLY HSMCppWrapper (NOT hsm_collector_core) — the native C ABI is resolved from the wrapper
    # DLL's re-exports, proving a consumer needs no second copy of the collector runtime.
    target_include_directories(WrapperConsole PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../native/collector/include)
    target_link_libraries(WrapperConsole PRIVATE HSMCppWrapper)
    if(MSVC)
        target_compile_options(WrapperConsole PRIVATE /EHsc)
    endif()
endif()

# Transport tests (#1848 part 5): the exported API of the wrapper against the scripted servers of
# the native collector's tests (a collector session with its table connection, and an HTTP recorder).
# They need the network transport and table support. The servers are plaintext loopback servers, so
# the tests drive a test build of the DLL, HSMCppWrapperTransportTest: the same sources with
# HSMWRAPPER_TRANSPORT_TESTS, which only lets the collector reach a plaintext server and shortens
# its package period (DataCollectorImpl.cpp, MakeOptions). It is never packed.
option(HSMWRAPPER_BUILD_TESTS "Build the wrapper transport tests" ON)
if(HSMWRAPPER_BUILD_TESTS AND HSM_COLLECTOR_HTTP AND HSM_COLLECTOR_TABLES)
    hsm_wrapper_add_dll(HSMCppWrapperTransportTest)
    target_compile_definitions(HSMCppWrapperTransportTest PRIVATE HSMWRAPPER_TRANSPORT_TESTS)

    add_executable(WrapperTransportTests WrapperConsole/WrapperTransportTests.cpp)
    target_include_directories(WrapperTransportTests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../native/collector/include
        ${CMAKE_CURRENT_SOURCE_DIR}/../native/collector/tests)
    target_link_libraries(WrapperTransportTests PRIVATE HSMCppWrapperTransportTest)
    if(WIN32)
        target_link_libraries(WrapperTransportTests PRIVATE ws2_32)
    endif()
    if(MSVC)
        target_compile_options(WrapperTransportTests PRIVATE /EHsc)
    endif()
    # Out of <build>/<config>/, whose DLLs packaging/pack.ps1 bundles as the wrapper's run-time closure.
    set_target_properties(HSMCppWrapperTransportTest WrapperTransportTests PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/transport-tests
        LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/transport-tests
        ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/transport-tests)

    enable_testing()
    foreach(_case IN ITEMS
            wrapper_default_transport_is_the_session
            wrapper_use_http_transport_sends_over_http_without_upgrade
            wrapper_use_http_transport_after_start_throws_and_keeps_the_session
            wrapper_start_async_then_use_http_transport_throws_and_keeps_the_session
            wrapper_stop_async_then_use_http_transport_applies_once_stopped
            wrapper_use_http_transport_after_stop_sends_over_http
            wrapper_use_http_transport_is_kept_across_restarts
            wrapper_stop_async_from_a_callback_during_stop_finishes
            wrapper_table_sensor_delivers_on_the_default_transport
            wrapper_table_sensor_is_unavailable_on_http)
        add_test(NAME ${_case} COMMAND WrapperTransportTests ${_case})
        # A hang (a lifecycle deadlock) must fail the case, not stall the runner. A passing case takes
        # a second or two; the waits inside give up after 30 s.
        set_tests_properties(${_case} PROPERTIES TIMEOUT 90)
    endforeach()
endif()
