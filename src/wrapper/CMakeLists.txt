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
endif()

# Statically pull the collector core + (where the CURL package is static) libcurl into the DLL so the
# only runtime dependency is the C/C++ runtime. With a shared CURL package, ship libcurl next to the
# DLL (the agent already vendors libcurl.dll / zlib1.dll under src/agent/packaging).

# In-process smoke that drives the public ABI exactly as a consumer would (no live server needed).
option(HSMWRAPPER_BUILD_SMOKE "Build the WrapperConsole smoke executable" ON)
if(HSMWRAPPER_BUILD_SMOKE)
    add_executable(WrapperConsole WrapperConsole/WrapperSmoke.cpp)
    target_include_directories(WrapperConsole PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)
    target_link_libraries(WrapperConsole PRIVATE HSMCppWrapper)
    if(MSVC)
        target_compile_options(WrapperConsole PRIVATE /EHsc)
    endif()
endif()
