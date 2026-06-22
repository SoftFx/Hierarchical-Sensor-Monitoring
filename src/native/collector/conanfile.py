import os

from conan import ConanFile
from conan.tools.cmake import CMake, CMakeDeps, CMakeToolchain, cmake_layout
from conan.tools.files import rmdir


class HsmCollectorConan(ConanFile):
    """Conan 2 recipe for the native HSM collector (#1100).

    Packages the static C-ABI core (`hsm_collector_core`) plus the header-only C++ RAII wrapper
    (`hsm_collector_cpp`). The package version tracks the C ABI semver (HSM_COLLECTOR_VERSION).
    Builds via the project's own CMakeLists (the same install() rules the find_package package
    uses); Conan owns the CMake integration, so consumers do:

        find_package(hsm_collector CONFIG REQUIRED)
        target_link_libraries(app PRIVATE hsm_collector::hsm_collector_cpp)
    """

    name = "hsm-collector"
    version = "0.4.0"
    license = "See repository (SoftFx/Hierarchical-Sensor-Monitoring)"
    url = "https://github.com/SoftFx/Hierarchical-Sensor-Monitoring"
    description = "Native C++ HSM DataCollector — stable C ABI core + header-only RAII C++ wrapper."
    topics = ("monitoring", "telemetry", "collector", "metrics", "hsm")

    settings = "os", "arch", "compiler", "build_type"
    options = {"http": [True, False], "fPIC": [True, False]}
    default_options = {"http": False, "fPIC": True}

    # Only the library sources — tests/examples are gated OFF for the package build.
    exports_sources = "CMakeLists.txt", "CMakePresets.json", "include/*", "src/*", "cmake/*"

    def config_options(self):
        if self.settings.os == "Windows":
            del self.options.fPIC

    def requirements(self):
        # libcurl is a private dependency of the static core, pulled in only for the HTTP transport.
        if self.options.http:
            self.requires("libcurl/8.10.1")

    def layout(self):
        cmake_layout(self)

    def generate(self):
        CMakeDeps(self).generate()
        tc = CMakeToolchain(self)
        tc.cache_variables["HSM_COLLECTOR_BUILD_TESTS"] = False
        tc.cache_variables["HSM_COLLECTOR_BUILD_EXAMPLES"] = False
        tc.cache_variables["HSM_COLLECTOR_INSTALL"] = True
        tc.cache_variables["HSM_COLLECTOR_HTTP"] = bool(self.options.http)
        tc.generate()

    def build(self):
        cmake = CMake(self)
        cmake.configure()
        cmake.build()

    def package(self):
        cmake = CMake(self)
        cmake.install()
        # Conan generates the CMake package config from the components below; drop the project's own
        # exported config so a consumer resolves a single, Conan-owned hsm_collector package.
        rmdir(self, os.path.join(self.package_folder, "lib", "cmake"))

    def package_info(self):
        self.cpp_info.set_property("cmake_file_name", "hsm_collector")

        core = self.cpp_info.components["core"]
        core.set_property("cmake_target_name", "hsm_collector::hsm_collector_core")
        core.libs = ["hsm_collector_core"]
        if self.settings.os in ("Linux", "FreeBSD"):
            core.system_libs.append("pthread")
        if self.settings.os == "Windows":
            # The Windows live metric readers (#1164) link PDH (perf counters); the recipe re-declares
            # components, so propagate it to consumers explicitly (the in-tree/vcpkg CMake export does
            # this automatically as a static-lib LINK_ONLY dependency).
            core.system_libs.append("pdh")
        if self.options.http:
            core.requires.append("libcurl::libcurl")

        # Header-only wrapper: no libs, just the include dir + a link to the core.
        cpp = self.cpp_info.components["cpp"]
        cpp.set_property("cmake_target_name", "hsm_collector::hsm_collector_cpp")
        cpp.requires = ["core"]
