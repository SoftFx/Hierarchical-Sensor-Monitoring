# Overlay port for the native HSM collector (#1100). It builds the library from the adjacent
# in-repo source tree (../), which lets CI verify the port against the current branch with
# --overlay-ports. For an upstream/registry publication, replace the SOURCE_PATH assignment with a
# vcpkg_from_github(REPO SoftFx/Hierarchical-Sensor-Monitoring REF <release-tag> SHA512 <hash>
# HEAD_REF master) block — the rest of the recipe is unchanged.
set(SOURCE_PATH "${CMAKE_CURRENT_LIST_DIR}/..")

vcpkg_check_features(OUT_FEATURE_OPTIONS FEATURE_OPTIONS
    FEATURES
        http HSM_COLLECTOR_HTTP
)

vcpkg_cmake_configure(
    SOURCE_PATH "${SOURCE_PATH}"
    OPTIONS
        -DHSM_COLLECTOR_BUILD_TESTS=OFF
        -DHSM_COLLECTOR_BUILD_EXAMPLES=OFF
        -DHSM_COLLECTOR_INSTALL=ON
        ${FEATURE_OPTIONS}
)

vcpkg_cmake_install()

# The project installs its package config to lib/cmake/hsm_collector; relocate it to the
# vcpkg-canonical share/hsm_collector and fix up the absolute paths.
vcpkg_cmake_config_fixup(PACKAGE_NAME hsm_collector CONFIG_PATH lib/cmake/hsm_collector)

# Header-only wrapper + static C core: no debug headers, and the static core has no DLLs/tools.
file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug/include")

# The repository ships no LICENSE file; record provenance as the package copyright.
vcpkg_install_copyright(FILE_LIST "${CMAKE_CURRENT_LIST_DIR}/copyright")
