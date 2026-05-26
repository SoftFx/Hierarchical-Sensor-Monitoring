# Collector break candidates

Дата: 2026-05-26.

Это история exploratory tests, которые смогли сломать `DataCollector`. После фикса удачные кандидаты переводятся в persistent regression tests и входят в обычный прогон.

## Найденные кандидаты

| Статус | Тест | Что ломало | Фактический результат до фикса | Почему важно |
| --- | --- | --- | --- | --- |
| Fixed, persistent regression | `Blocked_function_timer_callback_does_not_block_collector_stop` | Function sensor callback зависает и не возвращает управление | `Collector.Stop()` не завершался за `2 sec`, пока callback не был отпущен | Если пользовательский timer callback завис на внешнем API/локе/IO, остановка сервиса может зависнуть |
| Fixed, persistent regression | `Lifecycle_event_handler_exception_does_not_escape_collector_stop` | Пользовательский `ToStopped` handler кидает exception | `Collector.Stop()` возвращал наружу `InvalidOperationException` из event handler | Внешний обработчик lifecycle-события не должен ронять приложение через collector API |
| Fixed, persistent regression | `Data_sender_dispose_exception_does_not_escape_collector_dispose` | Пользовательский `IDataSender.Dispose()` кидает exception | `DataCollector.Dispose()` возвращал наружу `InvalidOperationException` из sender cleanup | Ошибка cleanup-а внешнего sender-а не должна ронять приложение при освобождении collector-а |

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
