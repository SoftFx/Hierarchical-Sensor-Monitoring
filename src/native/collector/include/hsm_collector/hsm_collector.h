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
    HSM_SENSOR_TYPE_STRING = 3,
    HSM_SENSOR_TYPE_INT_BAR = 4,
    HSM_SENSOR_TYPE_DOUBLE_BAR = 5,
    HSM_SENSOR_TYPE_FILE = 6,
    HSM_SENSOR_TYPE_RATE = 9,
    HSM_SENSOR_TYPE_ENUM = 10
} hsm_sensor_type_t;

/* User callbacks for function sensors. Invoked on the collector's scheduler thread OUTSIDE any
   collector/sensor lock (re-entering the same sensor from a callback is safe); they must not
   throw across the C ABI boundary. LIFETIME: `user_data` must outlive the COLLECTOR, not just
   the sensor handle — hsm_sensor_release frees only the handle, the collector keeps the sensor
   registered and the scheduler keeps invoking the callback until the collector is destroyed. */
typedef int32_t (*hsm_int_function_t)(void* user_data);
typedef int32_t (*hsm_int_values_function_t)(const int32_t* values, int32_t count, void* user_data);

typedef struct hsm_collector_options_t
{
    const char* access_key;
    const char* server_address;
    int32_t port;
    const char* module;
    const char* computer_name;
    /* Send-queue limits; 0 selects the default (20000 / 50 / 20ms — same as the C# collector
       conformance defaults). The queue evicts its oldest value when max_queue_size is exceeded. */
    int32_t max_queue_size;
    int32_t max_values_in_package;
    int32_t package_collect_period_ms;
} hsm_collector_options_t;

hsm_result_t hsm_collector_create(const hsm_collector_options_t* options, hsm_collector_t** out_collector);
void hsm_collector_destroy(hsm_collector_t* collector);

/* Lifecycle calls are NOT safe under concurrent invocation: drive Start/Stop/destroy from one
   thread (or serialize externally) — same assumption as the managed collector's lifecycle gate. */
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
hsm_result_t hsm_collector_create_enum_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor);
/* Sensor registration metadata (the AddOrUpdate command in the managed collector).
   Every sensor registers on every collector start, and immediately when created while
   the collector is running. The recorded registration JSON is the canonical
   cross-language registration text (see registration_contract.hsmtest). */
typedef struct hsm_enum_option_t
{
    int32_t key;
    const char* value;
    int32_t color;
    const char* description;
} hsm_enum_option_t;

/* ttl_ms <= 0 => no TTL; unit < 0 => unset (codes per the managed Unit enum);
   description may be NULL (instant sensors default to an empty description). */
hsm_result_t hsm_collector_create_int_sensor_with_options(
    hsm_collector_t* collector,
    const char* path,
    int64_t ttl_ms,
    int32_t unit,
    const char* description,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_enum_sensor_with_options(
    hsm_collector_t* collector,
    const char* path,
    const char* description,
    const hsm_enum_option_t* enum_options,
    size_t enum_option_count,
    hsm_sensor_t** out_sensor);

size_t hsm_collector_registration_count(const hsm_collector_t* collector);
hsm_result_t hsm_collector_get_registration_json(
    const hsm_collector_t* collector,
    size_t index,
    const char** out_json);

hsm_result_t hsm_collector_create_last_value_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int32_t default_value,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_last_value_bool_sensor(
    hsm_collector_t* collector,
    const char* path,
    bool default_value,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_last_value_double_sensor(
    hsm_collector_t* collector,
    const char* path,
    double default_value,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_last_value_string_sensor(
    hsm_collector_t* collector,
    const char* path,
    const char* default_value,
    hsm_sensor_t** out_sensor);

/* Bar sensors aggregate values into [open, close) windows aligned to bar_period_ms in unix-ms
   space. A bar is published when a value arrives past the current close time (roll-on-add) and
   when the collector stops with a non-empty bar (flush-on-stop). post_period_ms mirrors the C#
   option but periodic partial posting is outside the conformance contract and not implemented. */
hsm_result_t hsm_collector_create_int_bar_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t bar_period_ms,
    int64_t post_period_ms,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_double_bar_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t bar_period_ms,
    int64_t post_period_ms,
    int32_t precision,
    hsm_sensor_t** out_sensor);

/* Periodic sensors (rate / function): the FIRST post fires immediately on collector Start,
   then every post_period_ms. Rate = accumulated sum / measured elapsed seconds since the
   previous post (fallback: the configured period for the first sample); the sum resets on
   every post; status/comment are sticky from the last accepted increment. Stop does NOT
   flush the pending rate sum or trigger an extra function post. */
hsm_result_t hsm_collector_create_rate_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t post_period_ms,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_function_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t post_period_ms,
    hsm_int_function_t function,
    void* user_data,
    hsm_sensor_t** out_sensor);
/* Values-function: AddValue buffers into a sliding window (oldest evicted past
   max_cache_size); every post passes a snapshot of the window to the callback — the buffer
   is NOT drained. Values may be buffered before Start. */
hsm_result_t hsm_collector_create_values_function_int_sensor(
    hsm_collector_t* collector,
    const char* path,
    int64_t post_period_ms,
    int32_t max_cache_size,
    hsm_int_values_function_t function,
    void* user_data,
    hsm_sensor_t** out_sensor);
/* File sensor (string-content path only; disk reads are not part of the portable contract). */
hsm_result_t hsm_collector_create_file_sensor(
    hsm_collector_t* collector,
    const char* path,
    const char* default_file_name,
    const char* extension,
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
hsm_result_t hsm_sensor_add_enum(
    hsm_sensor_t* sensor,
    int32_t value,
    hsm_sensor_status_t status,
    const char* comment);

/* Rate increment: a non-finite value or an invalid status is silently dropped (the call
   returns HSM_RESULT_OK and neither the sum nor the sticky status/comment change). */
hsm_result_t hsm_sensor_add_rate(
    hsm_sensor_t* sensor,
    double value,
    hsm_sensor_status_t status,
    const char* comment);

/* Values-function buffer append (works before Start as well). */
hsm_result_t hsm_sensor_add_function_int(hsm_sensor_t* sensor, int32_t value);

/* File content publish: NULL content is silently ignored (returns HSM_RESULT_OK). */
hsm_result_t hsm_sensor_add_file(
    hsm_sensor_t* sensor,
    const char* utf8_content,
    hsm_sensor_status_t status,
    const char* comment);

/* Bar accumulation. A non-finite double value (NaN/Infinity) and an inconsistent partial
   (count < 1 or mean/first/last outside [min, max] — strict for int bars, FP-tolerant for
   double bars) are silently skipped: the call returns HSM_RESULT_OK and the bar is unchanged. */
hsm_result_t hsm_sensor_add_bar_int(hsm_sensor_t* sensor, int32_t value);
hsm_result_t hsm_sensor_add_bar_double(hsm_sensor_t* sensor, double value);
hsm_result_t hsm_sensor_add_bar_int_partial(
    hsm_sensor_t* sensor,
    int32_t min,
    int32_t max,
    int32_t mean,
    int32_t first,
    int32_t last,
    int32_t count);
hsm_result_t hsm_sensor_add_bar_double_partial(
    hsm_sensor_t* sensor,
    double min,
    double max,
    double mean,
    double first,
    double last,
    int32_t count);

/* Test/failure injection: the next `count` send attempts fail before recording anything;
   the queue re-enqueues the batch at the tail and retries on a later dispatch cycle. */
void hsm_collector_set_send_fail_next(hsm_collector_t* collector, int32_t count);

/* Test/failure injection: while enabled, every send attempt blocks until the stop path cancels
   in-flight sends (models a dead/black-holed transport). A cancelled hung send counts as a
   failed send, so the bounded stop drain drops the pending data instead of waiting it out —
   collector shutdown must never block the host's restart. */
void hsm_collector_set_send_hang(hsm_collector_t* collector, bool hang);

size_t hsm_collector_sent_count(const hsm_collector_t* collector);
hsm_result_t hsm_collector_get_sent_json(const hsm_collector_t* collector, size_t index, const char** out_json);

const char* hsm_collector_last_error(const hsm_collector_t* collector);

#ifdef __cplusplus
}
#endif
