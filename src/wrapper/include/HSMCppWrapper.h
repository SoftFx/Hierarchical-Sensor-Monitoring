#pragma once

// Consumer-side import attribute. Gated per-toolchain so the headers aren't MSVC-only on their face
// (the DLL itself is Windows-only today, but the macro shouldn't be the thing that breaks a build
// elsewhere). The build side defines this as the matching export attribute in pch.h.
#if defined(_MSC_VER)
#define HSMWRAPPER_API __declspec(dllimport)
#elif defined(__GNUC__)
#define HSMWRAPPER_API __attribute__((visibility("default")))
#else
#define HSMWRAPPER_API
#endif

#include "DataCollector.h"
#include "HSMEnums.h"
#include "HSMSensor.h"
#include "HSMBarSensor.h"
#include "HSMLastValueSensor.h"
#include "HSMParamsFuncSensor.h"
#include "HSMNoParamsFuncSensor.h"