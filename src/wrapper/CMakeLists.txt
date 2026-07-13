# Native HSMCppWrapper.dll
#
# Drop-in replacement for the C++/CLI HSMCppWrapper: same public headers (src/wrapper/include), same
# exported ABI, but the implementation runs over the native hsm::collector C++ API instead of the
# managed HSMDataCollector. Consumers (e.g. tt-aggregator2) relink against this DLL with no source
# changes and no longer drag in the CLR.

cmake_minimum_required(VERSION 3.16)
project(HSMCppWrapper CXX)

if(NOT CMAKE_CXX_STANDARD)
    set(CMAKE_CXX_STANDARD 17)
endif()
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# The wrapper drives a real HSM server, so it needs the libcurl HTTP transport. Default it on for the
# native collector subbuild; an explicit -DHSM_COLLECTOR_HTTP=OFF (e.g. for a compile-only check
# without CURL) still wins.
if(NOT DEFINED HSM_COLLECTOR_HTTP)
    set(HSM_COLLECTOR_HTTP ON CACHE BOOL "Build the libcurl HTTP transport (required by the wrapper at runtime)" FORCE)
endif()

# Bring in the native collector (provides hsm_collector_cpp -> hsm_collector_core).
if(NOT TARGET hsm_collector_cpp)
    add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../native/collector ${CMAKE_BINARY_DIR}/hsm_collector)
endif()

add_library(HSMCppWrapper SHARED
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
target_include_directories(HSMCppWrapper
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

# pch.h force-defines HSMWRAPPER_API as __declspec(dllexport) for this build; nothing else to do.
target_link_libraries(HSMCppWrapper PRIVATE hsm_collector_cpp)

if(MSVC)
    target_compile_options(HSMCppWrapper PRIVATE /EHsc /W3)
    # Emit a PDB for the Release DLL too (the VS Release config omits debug info by default, so the
    # handoff bundle otherwise ships Release without symbols for crash analysis at tt-aggregator2).
    # /Zi generates the debug info; the linker /DEBUG writes the .pdb; /OPT:REF /OPT:ICF restore the
    # size/speed folding that /DEBUG turns off, so Release stays optimized but symbolizable. Exports/
    # ABI are unaffected, so the relink stays byte-for-byte identical.
    target_compile_options(HSMCppWrapper PRIVATE $<$<CONFIG:Release>:/Zi>)
    target_link_options(HSMCppWrapper PRIVATE $<$<CONFIG:Release>:/DEBUG /OPT:REF /OPT:ICF>)
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
    target_sources(HSMCppWrapper PRIVATE ${_hsm_def})
endif()

# Statically pull the collector core + (where the CURL package is static) libcurl into the DLL so the
# only runtime dependency is the C/C++ runtime. With a shared CURL package, ship libcurl next to the
# DLL (the agent already vendors libcurl.dll / zlib1.dll under src/agent/packaging).

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
