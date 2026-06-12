cmake_minimum_required(VERSION 3.20)

project(HsmCollectorNativeSpike LANGUAGES CXX)

add_library(hsm_collector_spike STATIC
    src/hsm_collector.cpp
)

target_include_directories(hsm_collector_spike
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_features(hsm_collector_spike PUBLIC cxx_std_17)

if(MSVC)
    target_compile_options(hsm_collector_spike PRIVATE /W4 /permissive-)
else()
    target_compile_options(hsm_collector_spike PRIVATE -Wall -Wextra -Wpedantic)
endif()

add_executable(hsm_collector_spike_tests
    tests/hsm_collector_spike_tests.cpp
)

target_link_libraries(hsm_collector_spike_tests PRIVATE hsm_collector_spike)

enable_testing()

add_test(NAME native_invalid_argument_clears_out_params
    COMMAND hsm_collector_spike_tests native_invalid_argument_clears_out_params
)
add_test(NAME native_add_after_collector_destroy_is_rejected
    COMMAND hsm_collector_spike_tests native_add_after_collector_destroy_is_rejected
)
add_test(NAME native_sent_json_failure_reports_fresh_error
    COMMAND hsm_collector_spike_tests native_sent_json_failure_reports_fresh_error
)
add_test(NAME native_wrapper_sent_json_missing_throws_message
    COMMAND hsm_collector_spike_tests native_wrapper_sent_json_missing_throws_message
)

set(NATIVE_REGRESSION_TESTS
    native_create_rejects_null_server_address
    native_create_rejects_blank_server_address
    native_create_rejects_null_access_key
    native_create_rejects_blank_access_key
    native_slash_only_module_is_omitted_from_payload_path
    native_slash_only_computer_name_is_omitted_from_payload_path
    native_slash_only_module_and_computer_name_are_omitted_from_payload_path
    native_whitespace_only_path_is_rejected
    native_instant_string_null_value_is_invalid_and_not_sent
    native_last_value_string_null_default_is_invalid
    native_last_value_string_null_update_is_invalid_and_preserves_previous
    native_json_escapes_control_chars_in_string_value
    native_json_escapes_control_chars_in_comment
    native_json_escapes_control_chars_in_path
    native_json_escapes_control_chars_in_options_path_prefix
    native_double_nan_is_rejected_and_not_sent
    native_double_positive_infinity_is_rejected_and_not_sent
    native_double_negative_infinity_is_rejected_and_not_sent
    native_invalid_status_on_instant_value_is_rejected_and_not_sent
    native_invalid_status_on_last_value_preserves_previous_snapshot
)

foreach(name IN LISTS NATIVE_REGRESSION_TESTS)
    add_test(NAME ${name}
        COMMAND hsm_collector_spike_tests ${name}
    )
endforeach()

function(add_conformance_test name fixture)
    add_test(
        NAME ${name}
        COMMAND hsm_collector_spike_tests ${name}
                ${CMAKE_CURRENT_SOURCE_DIR}/../../../tests/conformance/collector/${fixture}
    )
endfunction()

add_conformance_test(conformance_instant_int_contract instant_int_contract.hsmtest)
add_conformance_test(conformance_lifecycle_int_contract lifecycle_int_contract.hsmtest)
add_conformance_test(conformance_stress_int_contract stress_int_contract.hsmtest)
add_conformance_test(conformance_stress_mixed_contract stress_mixed_contract.hsmtest)
add_conformance_test(conformance_value_int_contract value_int_contract.hsmtest)
add_conformance_test(conformance_cardinality_int_contract cardinality_int_contract.hsmtest)
add_conformance_test(conformance_instant_mixed_contract instant_mixed_contract.hsmtest)
add_conformance_test(conformance_last_value_contract last_value_contract.hsmtest)
add_conformance_test(conformance_enum_contract enum_contract.hsmtest)
add_conformance_test(conformance_bar_int_contract bar_int_contract.hsmtest)
add_conformance_test(conformance_bar_double_contract bar_double_contract.hsmtest)
add_conformance_test(conformance_bar_partial_contract bar_partial_contract.hsmtest)
add_conformance_test(conformance_bar_rollover_contract bar_rollover_contract.hsmtest)
add_conformance_test(conformance_queue_overflow_contract queue_overflow_contract.hsmtest)
add_conformance_test(conformance_sender_retry_contract sender_retry_contract.hsmtest)
add_conformance_test(conformance_flush_contract flush_contract.hsmtest)
add_conformance_test(conformance_rate_contract rate_contract.hsmtest)
add_conformance_test(conformance_function_contract function_contract.hsmtest)
add_conformance_test(conformance_file_contract file_contract.hsmtest)
