# Crypto value-source plugin example (#1164): an IMetricSource fetches BTCUSDT via libcurl and the
# collector streams it to a real server. HTTP build only (links libcurl directly for the plugin
# fetch, plus the collector's own transport via hsm_collector_cpp).
find_package(CURL REQUIRED)
add_executable(hsm_crypto_monitor main.cpp)
target_link_libraries(hsm_crypto_monitor PRIVATE hsm_collector_cpp CURL::libcurl)
target_compile_features(hsm_crypto_monitor PRIVATE cxx_std_17)
hsm_collector_set_warnings(hsm_crypto_monitor)
hsm_collector_apply_sanitizer(hsm_crypto_monitor)
