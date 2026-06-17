#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C"
{
#endif

/* ABI version. Bumped per the policy in docs/native-collector-c-abi.md: MINOR
   for additive, backward-compatible growth (new functions, struct fields
   appended); MAJOR for any breaking change (field reorder/removal, semantic
   change). hsm_collector_version() returns the packed value at runtime. */
#define HSM_COLLECTOR_VERSION_MAJOR 0
#define HSM_COLLECTOR_VERSION_MINOR 3
#define HSM_COLLECTOR_VERSION_PATCH 0
#define HSM_COLLECTOR_VERSION \
    ((HSM_COLLECTOR_VERSION_MAJOR * 10000) + (HSM_COLLECTOR_VERSION_MINOR * 100) + HSM_COLLECTOR_VERSION_PATCH)

typedef struct hsm_collector_t hsm_collector_t;
typedef struct hsm_sensor_t hsm_sensor_t;

/* Fixed underlying type so ANY int value is a valid object representation. The C ABI
   receives untrusted integers for enum-typed parameters (e.g. a caller passing a bad
   sensor status) and validates them at runtime; without a fixed type, merely loading an
   out-of-range value would be UB (caught by -fsanitize=enum). C++ and C23 support the
   syntax; older C is int-compatible already, so it falls back to a plain enum. */
#if defined(__cplusplus) || (defined(__STDC_VERSION__) && __STDC_VERSION__ >= 202311L)
#define HSM_ENUM_INT32 : int32_t
#else
#define HSM_ENUM_INT32
#endif

typedef enum hsm_result_t HSM_ENUM_INT32
{
    HSM_RESULT_OK = 0,
    HSM_RESULT_INVALID_ARGUMENT = 1,
    HSM_RESULT_INVALID_STATE = 2,
    HSM_RESULT_NOT_FOUND = 3,
    HSM_RESULT_LIMIT_EXCEEDED = 4,
    HSM_RESULT_INTERNAL_ERROR = 255
} hsm_result_t;

/* Collector lifecycle status. Mirrors the managed CollectorStatus state machine
   (overview.md "Lifecycle"): Stopped -> Starting -> Running -> Stopping ->
   Stopped, and Any-except-Disposed -> Disposed (terminal). */
typedef enum hsm_collector_status_t HSM_ENUM_INT32
{
    HSM_COLLECTOR_STATUS_STOPPED = 0,
    HSM_COLLECTOR_STATUS_STARTING = 1,
    HSM_COLLECTOR_STATUS_RUNNING = 2,
    HSM_COLLECTOR_STATUS_STOPPING = 3,
    HSM_COLLECTOR_STATUS_DISPOSED = 4
} hsm_collector_status_t;

typedef enum hsm_sensor_status_t HSM_ENUM_INT32
{
    HSM_SENSOR_STATUS_OFF_TIME = 0,
    HSM_SENSOR_STATUS_OK = 1,
    HSM_SENSOR_STATUS_WARNING = 2,
    HSM_SENSOR_STATUS_ERROR = 3
} hsm_sensor_status_t;

typedef enum hsm_sensor_type_t HSM_ENUM_INT32
{
    HSM_SENSOR_TYPE_BOOLEAN = 0,
    HSM_SENSOR_TYPE_INT = 1,
    HSM_SENSOR_TYPE_DOUBLE = 2,
    HSM_SENSOR_TYPE_STRING = 3,
    HSM_SENSOR_TYPE_INT_BAR = 4,
    HSM_SENSOR_TYPE_DOUBLE_BAR = 5,
    HSM_SENSOR_TYPE_FILE = 6,
    HSM_SENSOR_TYPE_TIMESPAN = 7,
    HSM_SENSOR_TYPE_VERSION = 8,
    HSM_SENSOR_TYPE_RATE = 9,
    HSM_SENSOR_TYPE_ENUM = 10
} hsm_sensor_type_t;

/* ---- Alert DSL (mirrors HSMDataCollector.Alerts) -------------------------------------------
   An alert is built before its sensor and attached at registration. The frozen enums below carry
   the EXACT numeric values of the managed AlertOperation/AlertProperty/AlertCombination/TargetType/
   AlertDestinationMode/AlertRepeatMode (HSMSensorDataObjects.SensorRequests) so the registration
   payload is byte-identical on the wire. */
typedef struct hsm_alert_t hsm_alert_t;

/* Which list the alert lands in: a data alert (instant/bar conditions) goes to AddOrUpdate.Alerts;
   a TTL alert (IfInactivityPeriodIs) goes to AddOrUpdate.TtlAlerts and its inactivity period also
   populates AddOrUpdate.TTLs (ticks). The instant/bar split is purely which condition properties
   are valid; both serialize into the same Alerts array. */
typedef enum hsm_alert_kind_t HSM_ENUM_INT32
{
    HSM_ALERT_KIND_INSTANT = 0,
    HSM_ALERT_KIND_BAR = 1,
    HSM_ALERT_KIND_TTL = 2
} hsm_alert_kind_t;

typedef enum hsm_alert_combination_t HSM_ENUM_INT32
{
    HSM_ALERT_COMBINATION_AND = 0,
    HSM_ALERT_COMBINATION_OR = 1
} hsm_alert_combination_t;

typedef enum hsm_alert_operation_t HSM_ENUM_INT32
{
    HSM_ALERT_OP_LESS_THAN_OR_EQUAL = 0,
    HSM_ALERT_OP_LESS_THAN = 1,
    HSM_ALERT_OP_GREATER_THAN = 2,
    HSM_ALERT_OP_GREATER_THAN_OR_EQUAL = 3,
    HSM_ALERT_OP_EQUAL = 4,
    HSM_ALERT_OP_NOT_EQUAL = 5,
    HSM_ALERT_OP_IS_CHANGED = 20,
    HSM_ALERT_OP_IS_ERROR = 21,
    HSM_ALERT_OP_IS_OK = 22,
    HSM_ALERT_OP_IS_CHANGED_TO_ERROR = 23,
    HSM_ALERT_OP_IS_CHANGED_TO_OK = 24,
    HSM_ALERT_OP_CONTAINS = 30,
    HSM_ALERT_OP_STARTS_WITH = 31,
    HSM_ALERT_OP_ENDS_WITH = 32,
    HSM_ALERT_OP_RECEIVED_NEW_VALUE = 50
} hsm_alert_operation_t;

typedef enum hsm_alert_property_t HSM_ENUM_INT32
{
    HSM_ALERT_PROP_STATUS = 0,
    HSM_ALERT_PROP_COMMENT = 1,
    HSM_ALERT_PROP_VALUE = 20,
    HSM_ALERT_PROP_MIN = 101,
    HSM_ALERT_PROP_MAX = 102,
    HSM_ALERT_PROP_MEAN = 103,
    HSM_ALERT_PROP_COUNT = 104,
    HSM_ALERT_PROP_LAST_VALUE = 105,
    HSM_ALERT_PROP_FIRST_VALUE = 106,
    HSM_ALERT_PROP_LENGTH = 120,
    HSM_ALERT_PROP_ORIGINAL_SIZE = 151,
    HSM_ALERT_PROP_NEW_SENSOR_DATA = 200,
    HSM_ALERT_PROP_EMA_VALUE = 210,
    HSM_ALERT_PROP_EMA_MIN = 211,
    HSM_ALERT_PROP_EMA_MAX = 212,
    HSM_ALERT_PROP_EMA_MEAN = 213,
    HSM_ALERT_PROP_EMA_COUNT = 214
} hsm_alert_property_t;

typedef enum hsm_alert_target_type_t HSM_ENUM_INT32
{
    HSM_ALERT_TARGET_CONST = 0,
    HSM_ALERT_TARGET_LAST_VALUE = 1
} hsm_alert_target_type_t;

typedef enum hsm_alert_destination_mode_t HSM_ENUM_INT32
{
    HSM_ALERT_DESTINATION_NOT_INITIALIZED = 1,
    HSM_ALERT_DESTINATION_EMPTY = 2,
    HSM_ALERT_DESTINATION_FROM_PARENT = 3,
    HSM_ALERT_DESTINATION_ALL_CHATS = 200
} hsm_alert_destination_mode_t;

typedef enum hsm_alert_repeat_mode_t HSM_ENUM_INT32
{
    HSM_ALERT_REPEAT_FIVE_MINUTES = 5,
    HSM_ALERT_REPEAT_TEN_MINUTES = 6,
    HSM_ALERT_REPEAT_FIFTEEN_MINUTES = 7,
    HSM_ALERT_REPEAT_THIRTY_MINUTES = 10,
    HSM_ALERT_REPEAT_HOURLY = 20,
    HSM_ALERT_REPEAT_DAILY = 50,
    HSM_ALERT_REPEAT_WEEKLY = 100
} hsm_alert_repeat_mode_t;

/* Built-in alert icons. hsm_alert_set_icon maps these to the same UTF-8 emoji the managed
   AlertIcon.ToUtf8() produces; pass an arbitrary emoji with hsm_alert_set_icon_raw instead. */
typedef enum hsm_alert_icon_t HSM_ENUM_INT32
{
    HSM_ALERT_ICON_OK = 0,
    HSM_ALERT_ICON_WARNING = 1,
    HSM_ALERT_ICON_ERROR = 2,
    HSM_ALERT_ICON_PAUSE = 3,
    HSM_ALERT_ICON_ARROW_UP = 10,
    HSM_ALERT_ICON_ARROW_DOWN = 11,
    HSM_ALERT_ICON_CLOCK = 100,
    HSM_ALERT_ICON_HOURGLASS = 101
} hsm_alert_icon_t;

/* User callbacks for function sensors. Invoked on the collector's scheduler thread OUTSIDE any
   collector/sensor lock (re-entering the same sensor from a callback is safe); they must not
   throw across the C ABI boundary, and must NOT call a collector lifecycle method
   (start/stop/dispose) — doing so from the scheduler thread would join that thread on itself.
   LIFETIME: `user_data` must outlive the COLLECTOR, not just the sensor handle —
   hsm_sensor_release frees only the handle, the collector keeps the sensor registered and the
   scheduler keeps invoking the callback until the collector is destroyed. */
typedef int32_t (*hsm_int_function_t)(void* user_data);
typedef int32_t (*hsm_int_values_function_t)(const int32_t* values, int32_t count, void* user_data);

/* Mirrors the managed CollectorOptions (public-api/feature.md). Every numeric
   field uses 0 to mean "take the managed default" shown in [brackets]; pass an
   explicit value to override. Validation (hsm_collector_create) rejects a blank
   access_key/server_address, a port outside 1..65535, and any negative numeric
   field. The DataSender transport seam is not exposed here — it arrives with the
   HTTP transport (#1096). */
typedef struct hsm_collector_options_t
{
    /* Connection. */
    const char* access_key;     /* required, non-blank */
    const char* server_address; /* required, non-blank */
    int32_t port;               /* 1..65535 */
    const char* client_name;    /* may be NULL */
    const char* module;         /* path prefix; may be NULL/blank */
    const char* computer_name;  /* path prefix; may be NULL/blank */

    /* Pipeline. The queue evicts its oldest value when max_queue_size is exceeded.
       NOTE: the managed package/period defaults dispatch every 15 s; the
       conformance harness sets a small package/period explicitly so the corpus
       stays fast — the 0-default here is the production value, not the test one. */
    int32_t max_queue_size;            /* [20000]  > 0 */
    int32_t max_values_in_package;     /* [1000]   > 0 */
    int32_t package_collect_period_ms; /* [15000]  > 0 */
    int32_t request_timeout_ms;        /* [30000]  > 0 */
    int32_t max_sensors;               /* [100000] > 0 — registration cap */

    /* Transport flags (consumed once the HTTP sender lands, #1096). */
    bool allow_untrusted_server_certificate; /* [false] */
    bool allow_plaintext_transport;          /* [false] */

    /* Error deduplication (see hsm_collector_set_logger). A window of 0 logs
       every message immediately with no dedup. */
    int64_t exception_deduplicator_window_ms; /* [3600000] >= 0 */
    int32_t max_deduplicated_messages;        /* [1000]    > 0 */
} hsm_collector_options_t;

/* Packed ABI version (see HSM_COLLECTOR_VERSION). Lets a consumer built against
   one header verify the linked library is compatible. */
int32_t hsm_collector_version(void);

hsm_result_t hsm_collector_create(const hsm_collector_options_t* options, hsm_collector_t** out_collector);
void hsm_collector_destroy(hsm_collector_t* collector);

/* Start/Stop should be driven from one thread (or serialized externally), same
   assumption as the managed collector. Both are idempotent: Start while Running
   and Stop while Stopped are no-ops returning OK. Start/Stop on a Disposed
   collector are rejected with HSM_RESULT_INVALID_STATE. */
hsm_result_t hsm_collector_start(hsm_collector_t* collector);
hsm_result_t hsm_collector_stop(hsm_collector_t* collector);

/* Current lifecycle status. Safe to call from any thread/state. */
hsm_collector_status_t hsm_collector_status(const hsm_collector_t* collector);

/* Dispose: terminal and idempotent, never fails, callable from any thread/state.
   Stops the collector if it is running — joining an in-flight Stop on another
   thread rather than issuing a duplicate, so exactly one stopped-notification
   fires and the terminal mode wins — then moves to Disposed. After dispose,
   create/start/stop/registration are rejected without crashing the host.
   destroy() still frees the handle; dispose() is the graceful terminal
   transition and may be called before destroy(). */
void hsm_collector_dispose(hsm_collector_t* collector);

/* TestConnection: callable in any lifecycle state (mirrors the managed
   ConnectionResult.IsOk). Returns OK when the sender reports the server
   reachable. Until the HTTP transport lands (#1096) the in-memory sender always
   reports reachable. */
hsm_result_t hsm_collector_test_connection(hsm_collector_t* collector);

/* Lifecycle observer (portable ILifecycleListener equivalent). The callback
   fires on the thread driving the transition, under the lifecycle lock, AFTER
   the status changes; only transitions after registration are delivered (no
   replay). A throwing/crashing callback is isolated — it can neither cross the
   C ABI boundary nor break the collector. The callback may read collector state
   and may add another listener, but must NOT call a lifecycle method
   (start/stop/dispose) — that thread already holds the lifecycle lock.
   `user_data` must outlive the collector. NULL callback returns INVALID_ARGUMENT. */
typedef void (*hsm_lifecycle_callback_t)(hsm_collector_status_t status, void* user_data);
hsm_result_t hsm_collector_add_lifecycle_listener(
    hsm_collector_t* collector,
    hsm_lifecycle_callback_t callback,
    void* user_data);

/* Pluggable log sink. Levels mirror the managed ICollectorLogger (debug/info/
   error). The callback is wrapped swallow-all: an exception escaping it is
   caught and dropped (a broken logger must never break the collector). Error
   messages pass through the MessageDeduplicator first (window/capacity from the
   options). Passing a NULL callback clears the sink. */
typedef enum hsm_log_level_t HSM_ENUM_INT32
{
    HSM_LOG_LEVEL_DEBUG = 0,
    HSM_LOG_LEVEL_INFO = 1,
    HSM_LOG_LEVEL_ERROR = 2
} hsm_log_level_t;
typedef void (*hsm_log_callback_t)(hsm_log_level_t level, const char* message, void* user_data);
hsm_result_t hsm_collector_set_logger(hsm_collector_t* collector, hsm_log_callback_t callback, void* user_data);

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
/* TimeSpan (type 7) and Version (type 8) instant sensors. TimeSpan values are 100-ns ticks
   (TimeSpan.Ticks); Version values are the four components (major.minor[.build[.revision]]) with
   a negative component meaning "absent" — both serialize exactly like the managed DTOs
   ("1.02:03:04.0050000" / "1.2.3.4"). */
hsm_result_t hsm_collector_create_timespan_sensor(
    hsm_collector_t* collector,
    const char* path,
    hsm_sensor_t** out_sensor);
hsm_result_t hsm_collector_create_version_sensor(
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

/* ---- Alert builders ------------------------------------------------------------------------
   Lifetime: an alert handle is owned by the collector and freed when the collector is destroyed
   (no separate release). Build conditions/actions, then attach to a sensor with
   hsm_sensor_attach_alert BEFORE the collector starts (or before the sensor is created while the
   collector is already running) — attaching rebuilds the sensor's registration payload, so an
   alert added after the registration was already emitted is not retroactively applied. A NULL
   handle argument returns INVALID_ARGUMENT; the builder setters never throw across the boundary. */
hsm_result_t hsm_collector_create_alert(
    hsm_collector_t* collector,
    hsm_alert_kind_t kind,
    hsm_alert_t** out_alert);

/* Append one condition. Conditions combine left-to-right with the given AND/OR combination
   (the first condition's combination is ignored by the server, mirroring the managed builder which
   always stamps And). target_value is the Const comparand as text (the managed DSL calls
   value.ToString()); it is ignored — and may be NULL — when target_type is LAST_VALUE. */
hsm_result_t hsm_alert_add_condition(
    hsm_alert_t* alert,
    hsm_alert_combination_t combination,
    hsm_alert_property_t property,
    hsm_alert_operation_t operation,
    hsm_alert_target_type_t target_type,
    const char* target_value);

/* Actions (AlertAction<T>). set_notification mirrors ThenSendNotification; set_scheduled_notification
   mirrors ThenSendScheduledNotification (time is unix-ms, serialized as ISO-8601-Z). set_icon maps a
   built-in AlertIcon to its emoji; set_icon_raw takes an arbitrary UTF-8 string. set_sensor_error
   raises the alert Status to Error. set_confirmation_period stores AndConfirmationPeriod (ms, encoded
   as ticks). set_disabled marks the alert IsDisabled (BuildAndDisable). For a TTL alert,
   set_inactivity_period sets the inactivity window (ms) that feeds TTLs/TtlAlerts. */
hsm_result_t hsm_alert_set_notification(
    hsm_alert_t* alert,
    const char* notification_template,
    hsm_alert_destination_mode_t destination);
hsm_result_t hsm_alert_set_scheduled_notification(
    hsm_alert_t* alert,
    const char* notification_template,
    int64_t time_unix_ms,
    hsm_alert_repeat_mode_t repeat_mode,
    bool instant_send,
    hsm_alert_destination_mode_t destination);
hsm_result_t hsm_alert_set_icon(hsm_alert_t* alert, hsm_alert_icon_t icon);
hsm_result_t hsm_alert_set_icon_raw(hsm_alert_t* alert, const char* utf8_icon);
hsm_result_t hsm_alert_set_sensor_error(hsm_alert_t* alert);
hsm_result_t hsm_alert_set_confirmation_period(hsm_alert_t* alert, int64_t period_ms);
hsm_result_t hsm_alert_set_disabled(hsm_alert_t* alert, bool disabled);
hsm_result_t hsm_alert_set_inactivity_period(hsm_alert_t* alert, int64_t period_ms);

/* Attach a built alert to a sensor and rebuild its registration payload. */
hsm_result_t hsm_sensor_attach_alert(hsm_sensor_t* sensor, hsm_alert_t* alert);

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
/* TimeSpan value = 100-ns ticks. Version value = components; pass -1 for an absent build/revision
   (Version.ToString() drops trailing absent components, so major.minor is the minimum). A NULL
   string is never produced — these always serialize a value. */
hsm_result_t hsm_sensor_add_timespan(
    hsm_sensor_t* sensor,
    int64_t ticks,
    hsm_sensor_status_t status,
    const char* comment);
hsm_result_t hsm_sensor_add_version(
    hsm_sensor_t* sensor,
    int32_t major,
    int32_t minor,
    int32_t build,
    int32_t revision,
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
