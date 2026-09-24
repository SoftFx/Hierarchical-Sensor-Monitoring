# Registry port for the native HSM collector (hsm-collector).
#
# Unlike the in-repo OVERLAY port (src/native/collector/vcpkg-port), which builds from the adjacent
# source tree so CI can verify the current branch, this port fetches a tagged source tarball. That
# makes this repository usable as a vcpkg registry: a consumer references it and runs
# `vcpkg install hsm-collector` with no checkout of the HSM sources.
#
# To publish a new version: push a `collector-v<semver>` tag, update REF + SHA512 below and the
# version in vcpkg.json, then run `vcpkg x-add-version hsm-collector` to refresh versions/.
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO SoftFx/Hierarchical-Sensor-Monitoring
    REF collector-v0.8.1
    SHA512 8b486c37c17abb21ed5780da3e61d3dd15de12f8846198db6b54e95d6ca0baa427c7d791a9f63b7c0952fb5ea57a8ee70ccfb1d2081c86ab4ff0b982ab7b2549
    HEAD_REF master
)

# The collector lives in a subdirectory of the repository tarball.
set(SOURCE_PATH "${SOURCE_PATH}/src/native/collector")

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

vcpkg_install_copyright(FILE_LIST "${CMAKE_CURRENT_LIST_DIR}/copyright")
