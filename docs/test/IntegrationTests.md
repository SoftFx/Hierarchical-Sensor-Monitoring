# Integration tests

Дата подготовки: 2026-06-01.

Интеграционные тесты проверяют end-to-end доставку sensor data от `DataCollector` до реального HSM сервера, запущенного в Docker. Тесты используют `HsmServerFixture` для управления контейнером, `CollectorOptionsHelper` для формирования уникальных sensor path-ов, и `ServerVerificationHelper` для чтения данных с сервера через REST API.

Все тестовые классы помечены `[Trait("Category", "Integration")]` и `[Collection("HSM Server")]` — xUnit запускает их последовательно в рамках одной collection.

## Где находятся тесты

```
src/collector/HSMDataCollector.IntegrationTests/Tests/
├── LifecycleTests.cs
├── SensorDataSendingTests.cs
├── BatchSendingTests.cs
├── ConcurrencyTests.cs
├── QueueBehaviorTests.cs
└── ConnectivityTests.cs
```

Проект: `src/collector/HSMDataCollector.IntegrationTests/HSMDataCollector.IntegrationTests.csproj`

Требования: запущенный Docker Desktop с HSM сервером (управляется через `HsmServerFixture`).

## Lifecycle

Назначение: проверить корректность переходов между состояниями `CollectorStatus` и порядок lifecycle events.

| Тест | Что проверяет | Критерии успеха |
| --- | --- | --- |
| `Start_SetsStatusToRunning` | Начальный статус `Stopped`, после `Start()` — `Running` | `collector.Status` переходит `Stopped → Running` |
| `Stop_SetsStatusToStopped` | После `Stop()` статус возвращается в `Stopped` | `collector.Status` переходит `Running → Stopped` |
| `LifecycleEvents_FireInCorrectOrder` | Порядок событий при `Start()` + `Stop()` | События в точном порядке: `Starting, Running, Stopping, Stopped` |
| `Restart_SendsDataSuccessfullyAfterRestart` | После `Start → Stop → Start` данные доходят до сервера | Int sensor со значением `99` — сервер получает ровно 1 значение `"99"` |
| `Dispose_StopsCollector` | `Dispose()` на запущенном коллекторе переводит в `Stopped` | `collector.Status == Stopped` после `Dispose()` |

## Sensor data sending

Назначение: проверить корректность доставки значений разных типов сенсоров до сервера и совпадение прочитанных данных с отправленными.

| Тест | Тип сенсора | Что проверяет | Критерии успеха |
| --- | --- | --- | --- |
| `SendBoolValue_ServerReceivesCorrectData` | Bool | `true` доходит до сервера | 1 значение `"True"` |
| `SendIntValue_ServerReceivesCorrectData` | Int | `42` доходит до сервера | 1 значение `"42"` |
| `SendDoubleValue_ServerReceivesCorrectData` | Double | `3.14` доходит до сервера | 1 значение `"3.14"` |
| `SendStringValue_ServerReceivesCorrectData` | String | `"hello world"` доходит до сервера | 1 значение `"hello world"` |
| `SendTimeSpanValue_ServerReceivesCorrectData` | Time | `TimeSpan.FromMinutes(5)` доходит до сервера | **SKIPPED**: Server bug #1068 |
| `SendVersionValue_ServerReceivesCorrectData` | Version | `new Version(1, 2, 3)` доходит до сервера | **SKIPPED**: Server bug #1068 |
| `SendRateValue_ServerReceivesCorrectData` | Rate | `100.0` доходит до сервера | Значение найдено через `WaitForValueAsync` |
| `SendIntBarValue_ServerReceivesCorrectData` | IntBar | Bar с значениями `10, 20, 30` | 1 bar: Min=`"10"`, Max=`"30"`, Mean=`"20"` |
| `SendDoubleBarValue_ServerReceivesCorrectData` | DoubleBar | Bar с значениями `1.5, 2.5, 3.5` | 1 bar: Min=`"1.5"`, Max=`"3.5"`, Mean=`"2.5"` |
| `SendEnumValue_ServerReceivesCorrectData` | Enum | `2` доходит до сервера | 1 значение `"2"` |
| `SendFileValue_ServerReceivesCorrectData` | File | Файл с именем `"test_file"`, расширением `"txt"` | `FileName == "test_file"`, `Extension == "txt"` |

Пропущенные тесты:
- `SendTimeSpanValue_ServerReceivesCorrectData` — серверный баг #1068: History API возвращает пустой результат для TimeSpan сенсоров.
- `SendVersionValue_ServerReceivesCorrectData` — серверный баг #1068: History API возвращает пустой результат для Version сенсоров.

## Batch sending

Назначение: проверить отправку нескольких значений за один проход и корректность разбиения больших пакетов.

| Тест | Что проверяет | Критерии успеха |
| --- | --- | --- |
| `SendMultipleValuesInList_ServerReceivesAll` | 4 сенсора разных типов (bool, int, double, string) отправляют по одному значению | Каждый сенсор: 1 значение с корректным строковым представлением |
| `SendLargeBatch_ExceedingMaxValuesInPackage_SentAsMultiplePackages` | 12 значений при `MaxValuesInPackage = 5` — данные разбиваются на несколько пакетов | Все 12 значений доставлены в порядке `["0", "1", ..., "11"]` |

## Concurrency

Назначение: проверить корректность отправки данных при параллельной работе нескольких сенсоров и при высокой нагрузке от одного сенсора.

| Тест | Что проверяет | Критерии успеха |
| --- | --- | --- |
| `MultipleSensorsSendingConcurrently_AllDataReceived` | 10 int сенсоров отправляют значения параллельно через `Task.Run` | Каждый сенсор: 1 значение `(i * 10).ToString()` |
| `HighVolumeSending_NoDataLoss` | 100 последовательных значений (0..99) от одного сенсора | Все 100 значений доставлены в точном порядке `["0", "1", ..., "99"]` за 120 сек |

## Queue behavior

Назначение: проверить поведение очереди — отбрасывание значений до старта, период сбора, overflow, приоритетные сенсоры.

| Тест | Что проверяет | Критерии успеха |
| --- | --- | --- |
| `ValuesDroppedBeforeStart_NotDeliveredAfterStart` | Значения, добавленные до `Start()`, не доставляются ни до, ни после старта | Пустой результат на сервере до и после `Start()` |
| `PackageCollectPeriod_WaitingPeriodIsRespected` | Значение не отправляется до истечения `PackageCollectPeriod = 5 sec` | Пусто через 2 сек; 1 значение `"10"` через 10 сек |
| `MaxQueueSize_OldestValuesDroppedOnOverflow` | При `MaxQueueSize = 5` и 10 значениях старшие (0-4) отбрасываются | Новейшие значения (5-9) доставлены |
| `PrioritySensor_DataSentImmediately` | Приоритетный сенсор (`IsPrioritySensor = true`) обходит таймерную отправку | 1 значение `"777"` через 10 сек |

## Connectivity

Назначение: проверить метод `TestConnection()` — корректные и ошибочные конфигурации.

| Тест | Что проверяет | Критерии успеха |
| --- | --- | --- |
| `TestConnection_WithValidServer_ReturnsOk` | Правильные параметры подключения | `result.IsOk == true` |
| `TestConnection_WithWrongPort_ReturnsError` | Неверный порт (fixture port + 1) | `result.IsOk == false`, `result.Error != null` |
| `TestConnection_WithInvalidAccessKey_ReturnsError` | Случайный GUID вместо access key | `result.IsOk == false` |
| `TestConnection_AfterServerRestart_ReturnsOk` | Подключение после остановки и перезапуска Docker контейнера | **SKIPPED**: Docker Desktop WSL2 не сохраняет port mappings после restart |

## Локальный запуск

```powershell
dotnet test .\src\collector\HSMDataCollector.IntegrationTests\HSMDataCollector.IntegrationTests.csproj --no-restore --logger "console;verbosity=detailed"
```

## Итого

| Файл | Тестов | Активных | Пропущенных |
| --- | ---: | ---: | ---: |
| LifecycleTests | 5 | 5 | 0 |
| SensorDataSendingTests | 11 | 9 | 2 (server bug #1068) |
| BatchSendingTests | 2 | 2 | 0 |
| ConcurrencyTests | 2 | 2 | 0 |
| QueueBehaviorTests | 4 | 4 | 0 |
| ConnectivityTests | 4 | 3 | 1 (Docker WSL2) |
| **Total** | **28** | **25** | **3** |
