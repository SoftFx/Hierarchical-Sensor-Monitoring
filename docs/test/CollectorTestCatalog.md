# Collector test catalog

Дата: 2026-05-26.

Короткий каталог тестов `HSMDataCollector`: группы, назначение и что сейчас покрыто. Подробные отчеты лежат рядом в `docs/test/`.

## 1. Transport chaos tests

Файл:

`src/collector/HSMDataCollector.Tests/CollectorTransportChaosTests.cs`

Документ:

`docs/test/CollectorTransportChaosTests.md`

Назначение: проверить устойчивость HTTP/TCP транспорта, отмену запросов, отсутствие зависших соединений после `Dispose()`.

Покрывает:

- сервер принимает и сразу закрывает соединение;
- сервер принимает и не отвечает;
- slow request body read;
- response headers пришли, body завис;
- malformed HTTP response;
- TCP reset во время request body;
- command endpoint зависает, data endpoint работает;
- data endpoint зависает, command endpoint работает;
- сервер сначала недоступен, потом появляется;
- много collectors на один flaky server;
- много collectors на разные flaky ports;
- huge string/comment payload;
- file sensor flood;
- `Dispose()` во время in-flight HTTP request;
- retry storm при постоянных disconnect.

## 2. Resource leak tests

Файл:

`src/collector/HSMDataCollector.Tests/CollectorResourceLeakTests.cs`

Документ:

`docs/test/CollectorResourceLeakTests.md`

Назначение: проверить, что под flaky HTTP нагрузкой не растут без ограничения ресурсы процесса.

Покрывает:

- `Process.HandleCount`;
- TCP connections к тестовым портам;
- TCP `ESTABLISHED`;
- TCP `TIME_WAIT`;
- managed memory после полного GC;
- private bytes;
- working set;
- thread count;
- request bytes;
- количество искусственных disconnect / HTTP 500 / slow responses.

## 3. Adversarial lifecycle tests

Файл:

`src/collector/HSMDataCollector.Tests/CollectorAdversarialTests.cs`

Документ:

`docs/test/CollectorAdversarialTests.md`

Назначение: сломать lifecycle коллектора, очереди и sensor API короткими точечными сценариями.

Покрывает:

- `RateSensor` после `double.NaN`;
- `Stop()` после legacy `Initialize(false)`;
- `Stop()` во время pending `Start(...)`;
- `Dispose()` при заблокированном sender;
- постоянные ошибки data sender;
- постоянные ошибки command sender;
- создание сенсоров после `Initialize()` при command failures;
- параллельные `AddValue()` во время `Dispose()`;
- overflow маленькой очереди;
- повторные циклы `Start()` / `Stop()`.

## 4. Flaky server stress tests

Файл:

`src/collector/HSMDataCollector.Tests/CollectorStressTests.cs`

Документ:

`docs/test/CollectorStressTests.md`

Назначение: нагрузочный HTTP stress через локальный HSM-like сервер, который отвечает `200`, `500`, медленно отвечает и рвет соединения.

Покрывает:

- параллельную отправку большого количества sensor values;
- регистрацию сенсоров через command requests;
- пакетную отправку data values;
- transient HTTP 500;
- broken connections;
- slow responses;
- gated 10-minute sustained load режим.

## 5. Existing default sensor smoke tests

Файл:

`src/collector/HSMDataCollector.Tests/DefaultSensorsTests.cs`

Назначение: исторический smoke-класс для default sensors.

Текущее состояние:

- тесты фактически пустые, assertions закомментированы;
- не дают реального покрытия системных сенсоров;
- требуют отдельной доработки или удаления/замены.

## Что пока покрыто слабо

Эти зоны стоит закрывать следующими тестами:

- реальные Windows default sensors: CPU, RAM, services, EventLog;
- реальные Unix default sensors: CPU/RAM/process sensors без shell-зависаний;
- сценарий "системный сенсор недоступен" или API ОС возвращает ошибку;
- права доступа: PerformanceCounter/EventLog/ServiceController без permissions;
- DNS/proxy/TLS certificate errors на настоящем `HttpClientHandler`;
- large file sensor около production лимитов;
- NLog/custom logger failures;
- точная проверка доставки всех значений при стабильном сервере;
- backwards compatibility публичного API под `net472`;
- behavior при неверных `CollectorOptions`.

## Быстрый полный прогон

Команда:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

Текущее состояние быстрого набора:

```text
Passed: 29
Skipped: 2
Failed: 0
Total: 31
Duration: ~28 seconds
```

Skipped - это длинные stress/soak тесты, которые запускаются явно через env-переменные.
