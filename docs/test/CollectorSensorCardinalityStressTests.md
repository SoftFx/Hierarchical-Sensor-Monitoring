# Collector sensor cardinality stress tests

Дата прогона: 2026-05-27.

Цель: проверить, что `CollectorOptions.MaxSensors = 100000` не является только формальным лимитом. Тесты проверяют две вещи:

- collector реально может зарегистрировать `100000` уникальных sensor path-ов;
- `100001`-й sensor path отклоняется синхронно, без hidden background exception и без ухода к OOM.

Код тестов:

`src/collector/HSMDataCollector.Tests/CollectorSensorCardinalityStressTests.cs`

## Группы тестов

| Группа | Тест | Что делает | Длительность | Запуск по умолчанию |
| --- | --- | --- | ---: | --- |
| Быстрый smoke | `High_cardinality_mixed_sensors_register_quickly_without_resource_spike` | Создает `5000` mixed sensors без `Start()`, проверяет скорость, threads и managed memory after GC | ~0.1-1 sec | Да |
| Boundary stress | `Default_max_sensors_allows_configured_boundary_and_rejects_next` | Создает `100000` mixed sensors, затем проверяет, что следующий sensor падает с `InvalidOperationException` | ~0.5-2 sec локально | Нет, нужен env |
| Nightly repeat | `Cardinality_registration_repeated_for_duration_stays_bounded` | Повторяет full boundary cycle по времени, после каждого цикла освобождает collector и проверяет ресурсный тренд после GC | 8 hours default; hard limit 8.5 hours | Нет, нужен env |

## Что именно создается

Каждый cardinality cycle создает смешанный набор сенсоров:

| Тип | Количество в `100000` cycle |
| --- | ---: |
| Instant sensors: `double`, `int`, `string`, `bool` | `50000` |
| Last-value double sensors | `12500` |
| Bar sensors: `int`, `double` | `25000` |
| Function sensors | `12500` |

Сенсоры создаются без `Start()`. Это сознательно отдельная проверка от timer stress:

- cardinality test проверяет размер storage, unique paths, options limit, disposal и memory trend;
- timer stress отдельно проверяет активные `PostDataPeriod` callbacks и CPU.

## Критерии падения

| Критерий | Почему важно |
| --- | --- |
| `100000` sensors не зарегистрировались | Значит default лимит выше фактической возможности collector-а |
| `100001`-й sensor не отклонен | Лимит не защищает от злоупотребления cardinality |
| Быстрый smoke дольше `10 sec` | Регистрация деградировала и может начать грузить CPU на обычном API usage |
| Thread count заметно растет при stopped sensors | Создание сенсоров не должно создавать thread-per-sensor |
| Managed memory after full GC растет между nightly cycles больше `128 MB` | Подозрение на managed leak после dispose/GC |
| Handle count растет между nightly cycles больше `200` | Подозрение на handle/resource leak |

## Как запустить

Быстрый обычный слой:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorSensorCardinalityStressTests"
```

Full boundary на `100000`:

```powershell
$env:HSM_COLLECTOR_RUN_CARDINALITY_STRESS="1"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-build --filter "FullyQualifiedName~Default_max_sensors_allows_configured_boundary_and_rejects_next" --logger "console;verbosity=normal"
```

Короткий локальный smoke ночного профиля:

```powershell
$env:HSM_COLLECTOR_RUN_CARDINALITY_NIGHTLY="1"
$env:HSM_COLLECTOR_CARDINALITY_NIGHTLY_SECONDS="30"
$env:HSM_COLLECTOR_CARDINALITY_NIGHTLY_MAX_SECONDS="120"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-build --filter "FullyQualifiedName~Cardinality_registration_repeated_for_duration_stays_bounded" --logger "trx;LogFileName=cardinality-nightly.trx"
```

Ночной профиль с default duration:

```powershell
$env:HSM_COLLECTOR_RUN_CARDINALITY_NIGHTLY="1"
$env:HSM_COLLECTOR_CARDINALITY_NIGHTLY_SENSORS="100000"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-build --filter "FullyQualifiedName~Cardinality_registration_repeated_for_duration_stays_bounded" --logger "trx;LogFileName=cardinality-nightly.trx"
```

Ночной профиль с явным duration:

```powershell
$env:HSM_COLLECTOR_RUN_CARDINALITY_NIGHTLY="1"
$env:HSM_COLLECTOR_CARDINALITY_NIGHTLY_SECONDS="28800"
$env:HSM_COLLECTOR_CARDINALITY_NIGHTLY_MAX_SECONDS="30600"
$env:HSM_COLLECTOR_CARDINALITY_NIGHTLY_SENSORS="100000"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-build --filter "FullyQualifiedName~Cardinality_registration_repeated_for_duration_stays_bounded" --logger "trx;LogFileName=cardinality-nightly.trx"
```

## Локальные результаты

Boundary stress:

```text
boundaryCardinalityStressResources; handles=590->612; threads=23->25; managedGc=2061128->61513064; private=41885696->118493184; workingSet=67698688->143024128; tcpEstablished=0->0; tcpTimeWait=0->0; tcpTotal=0->0
boundaryCardinalityStress; cycle=1; registered=100000; instant=50000; lastValue=12500; bar=25000; function=12500; overflowRejected=True; elapsedMs=448; commands=0; dataPackages=0; dataValues=0
```

30-секундный repeated профиль:

```text
nightlyCardinalityResources; handles=592->619; threads=23->25; managedGc=2061680->61946248; private=41918464->109359104; workingSet=67616768->135241728; tcpEstablished=0->0; tcpTimeWait=0->0; tcpTotal=0->0
nightlyCardinality; durationSeconds=30; maxSeconds=120; elapsedSeconds=30.9076645; cycles=38; sensorsPerCycle=100000; registeredSensors=3800000; overflowRejects=38; totalRegistrationMs=25023
```

Итог: за 30 секунд выполнено `38` полных cycles по `100000` mixed sensors, всего `3.8M` sensor registrations и `38` проверенных отказов на overflow. TCP-соединений нет, потому что эти тесты не стартуют sender loop; транспортные утечки проверяются в transport/resource suites.
