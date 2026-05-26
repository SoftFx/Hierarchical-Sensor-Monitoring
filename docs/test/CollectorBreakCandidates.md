# Collector break candidates

Дата: 2026-05-26.

Это список exploratory tests, которые уже смогли сломать `DataCollector`, но пока не включены как persistent regression tests в обычный прогон. Их можно запускать отдельно через:

```powershell
$env:HSM_COLLECTOR_RUN_BREAK_CANDIDATES="1"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~Exploratory_" --logger "console;verbosity=detailed"
Remove-Item Env:\HSM_COLLECTOR_RUN_BREAK_CANDIDATES
```

## Найденные кандидаты

| Статус | Тест | Что ломает | Фактический результат | Почему важно |
| --- | --- | --- | --- | --- |
| Breaks collector stop | `Exploratory_blocked_function_timer_callback_does_not_block_collector_stop` | Function sensor callback зависает и не возвращает управление | `Collector.Stop()` не завершился за `2 sec`, пока callback не был отпущен | Если пользовательский timer callback завис на внешнем API/локе/IO, остановка сервиса может зависнуть |

## Детали первого кандидата

Сценарий:

1. Создать function sensor с `PostDataPeriod=50 ms`.
2. Callback входит в `ManualResetEventSlim.Wait()` и не возвращает управление.
3. Дождаться, что callback реально стартовал.
4. Вызвать `collector.Stop()`.
5. Ожидать максимум `2 sec`.

Ожидаемое production-safe поведение: `Stop()` не должен ждать бесконечно пользовательский callback.

Текущее поведение: `Stop()` ждет текущий `ScheduledTask` callback, поэтому зависает до отпускания callback.

Кандидат на будущий persistent regression после фикса:

- сделать stop/dispose scheduler-а неблокирующим или ограниченным timeout-ом;
- после фикса убрать `BreakCandidateFact` и включить тест в обычный прогон.
