# Collector stability regression tests

Дата подготовки: 2026-06-01.

Цель: регрессионные тесты на конкретные баги, найденные при разработке `DataCollector`. Каждый тест воспроизводит условие, при котором collector ломался — зависал, терял данные, отправлял пустые пакеты или падал с исключением.

Код тестов:

`src/collector/HSMDataCollector.Tests/CollectorStabilityTests.cs`

Инфраструктура: внутренний `StabilityDataSender` (реализация `IDataSender` с настраиваемыми режимами сбоев), reflection-доступ к приватным полям (`_dataProcessor`, `_dataQueue`, `QueueCount`).

## Что проверяется

| Тест | Баг | Что ломаем | Критерии успеха |
| --- | --- | --- | --- |
| `Scheduler_loop_survives_unexpected_exception` | `CollectorScheduler.Loop()` ловит только `OperationCanceledException`; любое другое исключение убивает scheduler thread, все таймеры перестают тикать | Function sensor бросает `InvalidOperationException("boom")` каждые 50 ms; через 300 ms создается нормальный sensor | `throwCount > 0` (бросающий sensor сработал) и `goodCount > 0` (нормальный sensor продолжает работать после ошибки) |
| `DoubleMonitoringBar_CountAvr_computes_correct_average` | `DoubleMonitoringBar.CountAvr`: `first + second / 2` вычисляет `first + (second / 2)` вместо `(first + second) / 2` | Reflection-вызов `CountAvr(10.0, 20.0)` | Результат `15.0`, а не `20.0` |
| `Register_after_start_does_not_create_unobserved_task_exception` | `SensorsStorage.Register` вызывает `_ = AddAndStart(sensor)` при `IsStarted = true`; fire-and-forget task не обрабатывает исключения | `ThrowOnCommand = true`; создается sensor после `Start()`, что триггерит fire-and-forget; затем `GC.Collect` + `GC.WaitForPendingFinalizers` × 2 | `UnobservedTaskException` не сработал |
| `Processing_loop_recovers_after_send_failure` | `GetPackage()` dequeue-ит элементы до `SendDataAsync`; при ошибке отправки данные теряются, но цикл должен продолжить | `FailFirstNSends = 1`; 5 значений до ошибки, 5 значений после | `FailedSends >= 1` и `TotalDataValuesSent >= 5` |
| `Empty_package_not_sent_when_all_items_fail_validation` | `GetPackage()` dequeue-ит, фильтрует через `Validate()`, возвращает отфильтрованный список; если все элементы невалидны — отправляется пустая коллекция | Reflection-доступ к `_dataQueue`; 5 `IntBarSensorValue` с `Count = 0` (не проходят `Validate()`) | `ReceivedEmptyDataPackage == false` |
| `DefaultSensorsCollection_Dispose_does_not_throw_on_partial_registration` | `DefaultSensorsCollection.Dispose()` вызывает `QueueOverflowSensor.Dispose()` и `CollectorErrors.Dispose()` без null-conditional; `NullReferenceException` при неполной регистрации | `TestDefaultSensorsCollection` с `base(null, null)`, `IsCorrectOs => false` | `Dispose()` не бросает `NullReferenceException` |
| `Queue_count_stays_consistent_under_concurrent_access` | `_queueCount` отслеживается вручную через `Interlocked` параллельно с `ConcurrentQueue`; при конкурентном enqueue + GetPackage drain счетчик может разойтись с реальным размером очереди | `MaxQueueSize = 50`, `MaxValuesInPackage = 10`, `DataSendDelay = 10 ms`; 8 задач по 200 `AddValue` | `QueueCount >= 0` после drain (не уходит в минус) |

## Локальный запуск

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorStabilityTests" --logger "console;verbosity=detailed"
```

## Вывод

- Все 7 тестов закрывают конкретные регрессии, найденные при разработке и ревью.
- Тесты используют reflection для доступа к internal-структурам, что делает их чувствительными к рефакторингу, но позволяет проверять invariant-ы, недоступные через public API.
- `StabilityDataSender` позволяет моделировать разные режимы сбоев без запуска реального сервера.
