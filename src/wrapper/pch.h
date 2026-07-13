#pragma once

#include <string>
#include <memory>
#include <type_traits>
#include <iostream>
#include <chrono>
#include <functional>

// Build-side export attribute (consumers get the matching import from HSMCppWrapper.h). Gated
// per-toolchain to match the consumer header rather than hard-pinning MSVC.
#if defined(_MSC_VER)
#define HSMWRAPPER_API __declspec(dllexport)
#elif defined(__GNUC__)
#define HSMWRAPPER_API __attribute__((visibility("default")))
#else
#define HSMWRAPPER_API
#endif