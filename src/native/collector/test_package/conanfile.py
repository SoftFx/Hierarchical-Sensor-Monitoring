import os

from conan import ConanFile
from conan.tools.cmake import CMake, cmake_layout
from conan.tools.build import can_run


class HsmCollectorTestConan(ConanFile):
    """Clean-consumer proof for the hsm-collector Conan package (#1100): builds a tiny app purely
    through find_package(hsm_collector) provided by Conan, and runs it."""

    settings = "os", "arch", "compiler", "build_type"
    generators = "CMakeDeps", "CMakeToolchain"
    test_type = "explicit"

    def requirements(self):
        self.requires(self.tested_reference_str)

    def layout(self):
        cmake_layout(self)

    def build(self):
        cmake = CMake(self)
        cmake.configure()
        cmake.build()

    def test(self):
        if can_run(self):
            self.run(os.path.join(self.cpp.build.bindir, "hsm_consumer"), env="conanrun")
