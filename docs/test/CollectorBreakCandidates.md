# Collector break candidates

Дата: 2026-05-26.

Это история exploratory tests, которые смогли сломать `DataCollector`. После фикса удачные кандидаты переводятся в persistent regression tests и входят в обычный прогон.

## Найденные кандидаты

| Статус | Тест | Что ломало | Фактический результат до фикса | Почему важно |
| --- | --- | --- | --- | --- |
| Fixed, persistent regression | `Blocked_function_timer_callback_does_not_block_collector_stop` | Function sensor callback зависает и не возвращает управление | `Collector.Stop()` не завершался за `2 sec`, пока callback не был отпущен | Если пользовательский timer callback завис на внешнем API/локе/IO, остановка сервиса может зависнуть |
| Fixed, persistent regression | `Lifecycle_event_handler_exception_does_not_escape_collector_stop` | Пользовательский `ToStopped` handler кидает exception | `Collector.Stop()` возвращал наружу `InvalidOperationException` из event handler | Внешний обработчик lifecycle-события не должен ронять приложение через collector API |
| Fixed, persistent regression | `Data_sender_dispose_exception_does_not_escape_collector_dispose` | Пользовательский `IDataSender.Dispose()` кидает exception | `DataCollector.Dispose()` возвращал наружу `InvalidOperationException` из sender cleanup | Ошибка cleanup-а внешнего sender-а не должна ронять приложение при освобождении collector-а |
| Fixed, persistent regression | `Start_after_dispose_does_not_resurrect_collector` | После `Dispose()` вызывается `Start()` | Disposed collector переходил обратно в `Running` поверх закрытых ресурсов | Disposed collector должен оставаться `Stopped`; повторный старт после dispose является no-op |
| Fixed, persistent regression | `Initialize_after_dispose_does_not_resurrect_collector` | После `Dispose()` вызывается legacy `Initialize(false)` | Disposed collector переходил обратно в `Running` через старый API | Legacy initialize после dispose должен быть no-op |
| Fixed, persistent regression | `Values_added_while_stopped_are_not_sent_after_restart` | После `Stop()` пользователь продолжает писать в sensor, потом вызывает `Start()` | Stopped collector копил значения и отправлял stale payload после restart | Значения и хвосты очередей не должны переживать `Stop()` |
| Fixed, persistent regression | `Last_value_sensor_flushes_latest_value_on_stop` | `LastValueSensor` получил значение, затем collector остановили | Последнее значение не отправлялось на `Stop()` | Финальное значение должно уходить до остановки очередей; flush должен быть bounded |
| Fixed, persistent regression | `Last_value_sensor_flushes_latest_value_on_dispose` | `LastValueSensor` получил значение, затем активный collector освобождается через `Dispose()` | Последнее значение не отправлялось на `Dispose()` | Многие приложения закрывают collector через `Dispose()`; cleanup не должен терять финальное состояние |
| Fixed, persistent regression | `Stop_flush_does_not_resend_same_value_when_sender_does_not_enumerate_items` | Пользовательский `IDataSender` не перечисляет `IEnumerable` items, переданный из очереди | Один queued value на `Stop()` вызвал 3 852 549 повторных `SendDataAsync` за 1 секунду | Очередь не должна зависеть от реализации sender-а; иначе возможен CPU spike и повторная отправка одного payload |
| Fixed, persistent regression | `Stop_during_slow_data_sends_completes_without_parallel_flush` | Медленный `IDataSender` во время `Stop()` | Flush мог конкурировать с обычным data processing loop за sender и очередь | Остановка должна иметь один контролируемый send path и bounded cleanup |
| Fixed, persistent regression | `Concurrent_start_calls_initialize_collector_once` | 50 параллельных `Start()` на одном collector-е | Lifecycle transition `Stopped -> Starting` не был атомарно защищен | Повторный старт не должен плодить initialization/queue activity |
| Fixed, persistent regression | `Stop_with_data_sender_that_ignores_cancellation_does_not_hang` | Пользовательский `IDataSender` зависает в `SendDataAsync` и игнорирует `CancellationToken` | `Collector.Stop()` не завершался за `2 sec`, пока sender не был вручную отпущен | Внешняя реализация sender-а не должна навсегда подвешивать сервис при остановке collector-а |

Текущий persistent test находится в `src/collector/HSMDataCollector.Tests/CollectorAdversarialTests.cs`.

## Детали первого кандидата

Сценарий:

1. Создать function sensor с `PostDataPeriod=50 ms`.
2. Callback входит в `ManualResetEventSlim.Wait()` и не возвращает управление.
3. Дождаться, что callback реально стартовал.
4. Вызвать `collector.Stop()`.
5. Ожидать максимум `2 sec`.

Ожидаемое production-safe поведение: `Stop()` не должен ждать бесконечно пользовательский callback.

Поведение до фикса: `Stop()` ждал текущий `ScheduledTask` callback, поэтому зависал до отпускания callback.

Фикс:

- `Stop/Dispose` scheduled task снимает задачу с расписания, но не ждет зависший текущий callback;
- `RestartTimer` продолжает ждать текущий callback, чтобы сохранить защиту от callback overlap;
- тест включен в обычный persistent-прогон.

## Детали кандидата с sender cancellation

Сценарий:

1. Создать collector с `RequestTimeout=100 ms`.
2. Подставить тестовый `IDataSender`, который входит в `SendDataAsync`, фиксирует прием пакета и затем ждет внешний сигнал, полностью игнорируя `CancellationToken`.
3. Запустить collector и отправить одно значение.
4. Убедиться, что sender реально вошел в отправку.
5. Вызвать `collector.Stop()` и ожидать максимум `2 sec`.

Ожидаемое production-safe поведение: `Stop()` должен завершиться в bounded-время, даже если кастомный sender некорректно игнорирует cancellation.

Поведение до фикса: queue processor отменял token и затем бесконечно ждал свою worker-задачу.

Фикс:

- ожидание остановки queue worker-а ограничено `CollectorOptions.RequestTimeout`;
- при timeout очередь может быть очищена, но зависший worker не считается остановленным;
- повторный `Start()` не запускает второй worker поверх старого зависшего worker-а.
