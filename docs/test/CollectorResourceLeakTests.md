# Collector resource leak tests

Дата прогона: 2026-05-26.

Коротко: добавлены focused ресурсные adversarial-тесты, которые гоняют `HSMDataCollector` против локального flaky HTTP-сервера и после каждого цикла проверяют, что не остаются активные TCP-соединения, не растут без ограничения handles, threads, managed memory после GC, private bytes и working set.

Важно: это не единственная resource leak проверка и не замена suite-level gate. Каждый gated suite сам снимает ресурсы до/после своего прогона; этот файл нужен как отдельная focused HTTP/resource нагрузка.

Подробный код тестов:

`src/collector/HSMDataCollector.Tests/CollectorResourceLeakTests.cs`

## Что измеряем

На каждом цикле снимаются метрики до запуска коллектора и после `Dispose()`, остановки fake-сервера и полного GC:

- `Process.HandleCount`;
- TCP-соединения к тестовым портам;
- TCP `ESTABLISHED`;
- TCP `TIME_WAIT`;
- managed memory после `GC.Collect()` + `GC.WaitForPendingFinalizers()` + повторного `GC.Collect()`;
- `Process.PrivateMemorySize64`;
- `Process.WorkingSet64`;
- `Process.Threads.Count`;
- объем реально принятых HTTP request body;
- количество request-ов, command/data request-ов;
- количество искусственных `Response.Abort()`;
- количество искусственных `500 Internal Server Error`;
- количество slow responses.

## Как контролируем, что ничего не утекло

Основные правила проверки:

| Метрика | Правило |
| --- | --- |
| TCP `ESTABLISHED` к тестовым портам | После `Dispose()` и остановки сервера должно быть `0` на каждом цикле |
| TCP `TIME_WAIT` | Допускается, потому что это нормальное состояние TCP после закрытия; ограничено верхним пределом `< 1000` |
| Managed memory после полного GC | Сравнивается тренд от первого завершенного цикла до последнего; рост должен быть меньше `64 MB` в быстром тесте и `128 MB` в длинном |
| `Process.HandleCount` | После прогрева рост от первого завершенного цикла до последнего должен быть меньше `250` |
| `Process.Threads.Count` | Рост должен быть меньше `20` |
| Private bytes | Рост должен быть меньше `256 MB` |
| Working set | Рост должен быть меньше `256 MB` |
| Flaky-сценарии сервера | В каждом цикле должны реально произойти request-ы, обрывы соединения, HTTP 500 и slow responses |

Важно: первый цикл считается прогревочным для CLR, HttpClient, ThreadPool и testhost. Поэтому тренд handles/memory сравнивается не с самым началом процесса, а с состоянием после первого завершенного цикла.

## Быстрый ресурсный тест

Тест:

`Collector_releases_resources_after_flaky_http_cycles`

Он запускается в обычном `dotnet test`.

План нагрузки:

| Параметр | Значение |
| --- | --- |
| Циклов | 5 |
| Сенсоров на цикл | 24 |
| Worker-задач | 8 |
| `AddValue()` на worker | 600 |
| Всего `AddValue()` на цикл | 4 800 |
| Всего `AddValue()` за тест | 24 000 |
| Max values in package | 100 |
| Max queue size | 20 000 |
| Package collect period | 75 ms |
| HTTP timeout | 2 секунды |

Flaky-сервер:

| Сценарий | Частота |
| --- | --- |
| Первые успешные запросы | 3 |
| HTTP 500 | каждый 5-й request |
| Обрыв соединения | каждый 9-й request |
| Slow response | каждый 7-й request |
| Slow delay | 150 ms |

Команда:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorResourceLeakTests.Collector_releases_resources_after_flaky_http_cycles" --logger "console;verbosity=detailed"
```

Результат локального прогона:

```text
Passed: 1
Failed: 0
Duration: 4 s
```

Суммарные счетчики:

| Метрика | Значение |
| --- | --- |
| Total cycles | 5 |
| Total HTTP requests | 131 |
| Data requests | 124 |
| Command requests | 7 |
| Artificial connection aborts | 14 |
| Artificial HTTP 500 responses | 23 |
| Artificial slow responses | 18 |
| Total request bytes | 2 229 008 |

Ресурсный тренд после прогрева:

| Метрика | Первый завершенный цикл | Последний цикл | Изменение |
| --- | ---: | ---: | ---: |
| HandleCount | 984 | 1017 | +33 |
| Threads | 66 | 67 | +1 |
| Managed after full GC | 4 318 272 | 4 284 832 | -33 440 |
| Private bytes | 57 614 336 | 60 030 976 | +2 416 640 |
| Working set | 84 099 072 | 86 646 784 | +2 547 712 |
| TCP ESTABLISHED | 0 | 0 | 0 |
| TCP TIME_WAIT | 1 | 1 | 0 |

## Длинный ресурсный тест

Тест:

`Collector_releases_resources_after_long_flaky_http_cycles`

По умолчанию он пропущен. Запускается только при:

```powershell
$env:HSM_COLLECTOR_RUN_RESOURCE_LEAK_STRESS='1'
```

По умолчанию длинный тест делает `30` циклов. Для локальной проверки количество циклов можно задать:

```powershell
$env:HSM_COLLECTOR_RESOURCE_LEAK_CYCLES='10'
```

План нагрузки для проверенного запуска на 10 циклов:

| Параметр | Значение |
| --- | --- |
| Циклов | 10 |
| Сенсоров на цикл | 48 |
| Worker-задач | 40 |
| `AddValue()` на worker | 1000 |
| Всего `AddValue()` на цикл | 40 000 |
| Всего `AddValue()` за тест | 400 000 |
| Max values in package | 150 |
| Max queue size | 50 000 |
| Package collect period | 75 ms |
| HTTP timeout | 2 секунды |

Flaky-сервер такой же, как в быстром тесте:

| Сценарий | Частота |
| --- | --- |
| Первые успешные запросы | 3 |
| HTTP 500 | каждый 5-й request |
| Обрыв соединения | каждый 9-й request |
| Slow response | каждый 7-й request |
| Slow delay | 150 ms |

Команда проверенного запуска:

```powershell
$env:HSM_COLLECTOR_RUN_RESOURCE_LEAK_STRESS='1'
$env:HSM_COLLECTOR_RESOURCE_LEAK_CYCLES='10'
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorResourceLeakTests.Collector_releases_resources_after_long_flaky_http_cycles" --logger "console;verbosity=detailed"
Remove-Item Env:\HSM_COLLECTOR_RUN_RESOURCE_LEAK_STRESS
Remove-Item Env:\HSM_COLLECTOR_RESOURCE_LEAK_CYCLES
```

Результат локального запуска на 10 циклов:

```text
Passed: 1
Failed: 0
Duration: 8 s
```

Суммарные счетчики:

| Метрика | Значение |
| --- | --- |
| Total cycles | 10 |
| Total HTTP requests | 273 |
| Data requests | 258 |
| Command requests | 15 |
| Artificial connection aborts | 29 |
| Artificial HTTP 500 responses | 49 |
| Artificial slow responses | 39 |
| Total request bytes | 6 969 170 |

Ресурсный тренд после прогрева:

| Метрика | Первый завершенный цикл | Последний цикл | Изменение |
| --- | ---: | ---: | ---: |
| HandleCount | 1000 | 1033 | +33 |
| Threads | 68 | 69 | +1 |
| Managed after full GC | 7 192 608 | 7 169 376 | -23 232 |
| Private bytes | 60 604 416 | 65 941 504 | +5 337 088 |
| Working set | 87 371 776 | 93 077 504 | +5 705 728 |
| TCP ESTABLISHED | 0 | 0 | 0 |
| TCP TIME_WAIT | 0 | 0 | 0 |

## Обычный полный test run

Команда:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore
```

Результат после добавления ресурсных тестов:

```text
Passed: 14
Skipped: 2
Failed: 0
Total: 16
Duration: 4 s
```

Пропущены два длинных теста:

- flaky-server 10-minute stress;
- long resource leak stress.

## Что еще важно

Эти тесты проверяют утечки на стороне collector/testhost процесса и TCP-соединения к локальным тестовым портам. Они не доказывают отсутствие утечек внутри реального HSM-сервера.

Если нужен максимально жесткий soak, стоит запускать long resource leak stress на 30-100 циклов и сохранять detailed output в CI artifact.

## Focused repeat

Тест:

`Focused_resource_leak_load_repeated_for_duration_stays_bounded`

Это focused повторялка resource-load сценария. Она не считается обычным suite: обычные suite сами делают resource snapshot вокруг своих сценариев.

Запуск:

```powershell
$env:HSM_COLLECTOR_RUN_RESOURCE_LEAK_SOAK='1'
$env:HSM_COLLECTOR_SUITE_SOAK_SECONDS='30'
$env:HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS='120'
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~Focused_resource_leak_load_repeated_for_duration_stays_bounded" --logger "console;verbosity=detailed"
Remove-Item Env:\HSM_COLLECTOR_RUN_RESOURCE_LEAK_SOAK
Remove-Item Env:\HSM_COLLECTOR_SUITE_SOAK_SECONDS
Remove-Item Env:\HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS
```

Пример последнего 30-секундного запуска:

```text
focusedResourceLeakLoadResources; handles=1068->1016; threads=63->52; managedGc=5119800->5823000; private=73334784->73433088; workingSet=100524032->100904960; tcpEstablished=0->0; tcpTimeWait=0->0; tcpTotal=0->0
focusedResourceLeakLoad; durationSeconds=30; maxSeconds=120; elapsedSeconds=32.5019163; loadCycles=8; resourceCycles=40; addValues=192000; requests=1120; commands=53; data=1067; aborts=120; failures=200; slow=160; bytes=19045314
```

`commands` здесь означает command/registration requests, а не отдельный login endpoint.

## Suite-level leak check

Общий 30-секундный suite soak проверяет ресурсы вокруг каждого обычного suite. Подробный общий отчет: [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md).
