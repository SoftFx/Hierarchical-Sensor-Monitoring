#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct hsm_collector_t hsm_collector_t;
typedef struct hsm_sensor_t hsm_sensor_t;

typedef enum hsm_result_t
{
    HSM_RESULT_OK = 0,
    HSM_RESULT_INVALID_ARGUMENT = 1,
    HSM_RESULT_INVALID_STATE = 2,
    HSM_RESULT_NOT_FOUND = 3,
    HSM_RESULT_INTERNAL_ERROR = 255
} hsm_result_t;

typedef enum hsm_sensor_status_t
{
    HSM_SENSOR_STATUS_OFF_TIME = 0,
    HSM_SENSOR_STATUS_OK = 1,
    HSM_SENSOR_STATUS_WARNING = 2,
    HSM_SENSOR_STATUS_ERROR = 3
} hsm_sensor_status_t;

typedef enum hsm_sensor_type_t
{
    HSM_SENSOR_TYPE_BOOLEAN = 0,
    HSM_SENSOR_TYPE_INT = 1,
    HSM_SENSOR_TYPE_DOUBLE = 2,
    HSM_SENSOR_TYPE_STRING = 3
} hsm_sensor_type_t;

typedef struct hsm_collector_options_t
{
    const char* access_key;
    const char* server_address;
    int32_t port;
    const char* module;
    const char* computer_name;
} hsm_collector_options_t;

hsm_result_t hsm_collector_create(const hsm_collector_options_t* options, hsm_collector_t** out_collector);
void hsm_collector_destroy(hsm_collector_t* collector);

hsm_result_t hsm_collector_start(hsm_collector_t* collector);
hsm_result_t hsm_collector_stop(hsm_collector_t* collector);

hsm_result_t hsm_collector_create_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_bool_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_double_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_string_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor);

void hsm_sensor_release(hsm_sensor_t* sensor);

hsm_result_t hsm_sensor_add_int(
    hsm_sensor_t* sensor,
    int32_t value,
    hsm_sensor_status_t status,
    const char* comment);
hsm_result_t hsm_sensor_add_bool(
    hsm_sensor_t* sensor,
    bool value,
    hsm_sensor_status_t status,
    const char* comment);
hsm_result_t hsm_sensor_add_double(
    hsm_sensor_t* sensor,
    double value,
    hsm_sensor_status_t status,
    const char* comment);
hsm_result_t hsm_sensor_add_string(
    hsm_sensor_t* sensor,
    const char* value,
    hsm_sensor_status_t status,
    const char* comment);

size_t hsm_collector_sent_count(const hsm_collector_t* collector);
hsm_result_t hsm_collector_get_sent_json(const hsm_collector_t* collector, size_t index, const char** out_json);

const char* hsm_collector_last_error(const hsm_collector_t* collector);

#ifdef __cplusplus
}
#endif
