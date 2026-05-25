# Collector suite soak tests

Дата прогона: 2026-05-26.

Цель: прогнать каждую группу тестов не один раз, а повторять весь suite по кругу в течение заданного времени. На текущий момент default duration - `30` секунд на suite.

Важно: `Resource leak` больше не трактуется как отдельный единственный suite. Каждый обычный suite сам делает ресурсный контроль:

1. снимает baseline ресурсов перед началом suite;
2. гоняет свои сценарии по кругу до soft target;
3. снимает финальный snapshot ресурсов;
4. пишет счетчики нагрузки;
5. проверяет, что рост ресурсов не стал критическим.

Тесты выполняются последовательно. Для этого test assembly отключает параллельное выполнение через `TestAssemblyInfo.cs`, чтобы TCP/resource метрики разных suite не смешивались.

## Команда запуска

```powershell
$env:HSM_COLLECTOR_RUN_SUITE_SOAK="1"
$env:HSM_COLLECTOR_SUITE_SOAK_SECONDS="30"
$env:HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS="120"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~suite_repeated" --logger "console;verbosity=detailed"
```

`HSM_COLLECTOR_SUITE_SOAK_SECONDS` - soft target. Suite не обязан оборваться ровно на 30-й секунде: если цикл уже начался, он корректно дорабатывает.

`HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS` - hard safety limit. Default: `120` секунд. После каждого suite cycle или transport phase тест проверяет elapsed time; если hard limit превышен, тест падает как зависший.

## Что измеряется вокруг каждого suite

| Метрика | Как контролируется |
| --- | --- |
| `Process.HandleCount` | Snapshot до/после suite; рост должен быть меньше `500` handles |
| `Process.Threads.Count` | Snapshot до/после suite; рост должен быть меньше `80` threads |
| Managed memory after full GC | До/после вызывается полный GC; рост должен быть меньше `128 MB` |
| Private bytes | Рост должен быть меньше `256 MB` |
| Working set | Рост должен быть меньше `256 MB` |
| TCP `ESTABLISHED` к тестовым портам | Для transport/stress/resource suite должно быть `0` после suite |
| TCP `TIME_WAIT` к тестовым портам | Допускается как нормальное TCP-состояние, но bounded: `< 2000` для общих suite и `< 1000` в transport/resource специализированных проверках |
| Объем нагрузки | Suite пишет количество циклов, `AddValue()`, command/registration requests, data requests, failed/aborted/slow requests и bytes, если применимо |

В этих тестах нет отдельного login endpoint. Когда в отчетах нужен аналог "сколько раз логинились", используем `commands`: это command/registration requests, которые collector отправляет при регистрации сенсоров.

## Итог

```text
Total tests: 5
Passed: 5
Failed: 0
Total time: 2.6340 minutes
```

## Результаты по suite

| Suite | Тест | Длительность | Сколько успел пройти | Нагрузка | Ресурсный итог | Результат |
| --- | --- | ---: | ---: | --- | --- | --- |
| Default sensor smoke | `Default_sensor_smoke_suite_repeated_for_duration` | 30 sec | 1922 cycles / 3844 scenario runs | `addValues=0`, `requests=0`, smoke-only | handles `683->680`, threads `30->29`, managed GC `3604480->3999960` | Passed |
| Adversarial lifecycle | `Adversarial_suite_repeated_for_duration_stays_green` | 30 sec | 16 cycles / 160 scenario runs | `addValues=432176`, `sensorCreates=3312`, `dataFailureBursts=16`, `commandFailureBursts=32` | handles `679->838`, threads `29->57`, managed GC `4043624->34809320` | Passed |
| Flaky server stress | `Flaky_server_stress_suite_repeated_for_duration_stays_green` | 30 sec | 10 cycles | `addValues=192000`, `requests=825`, `commands=15`, `data=810`, `failures=110`, `aborts=40`, `slow=70`, `bytes=33253204` | handles `840->1068`, threads `57->63`, `tcpEstablished=0`, `tcpTimeWait=3` | Passed |
| Resource leak load | `Resource_leak_suite_repeated_for_duration_stays_bounded` | 30 sec | 8 suite cycles / 40 resource cycles | `addValues=192000`, `requests=1120`, `commands=53`, `data=1067`, `aborts=120`, `failures=200`, `slow=160`, `bytes=19045314` | handles `1068->1016`, threads `63->52`, `tcpEstablished=0`, `tcpTimeWait=0` | Passed |
| Transport chaos | `Mixed_transport_chaos_suite_repeated_on_one_server_stays_bounded` | 30 sec | 21 mixed cycles | `addValues=244000`, `accepted=1150`, `requests=1148`, `commands=1125`, `data=19`, `dropped=321`, `hung=171`, `resets=160` | handles `1016->1097`, threads `52->64`, `tcpEstablished=0`, `tcpTimeWait=820` | Passed |

## Detailed Output Highlights

```text
defaultSensorSmokeSuiteSoakResources; handles=683->680; threads=30->29; managedGc=3604480->3999960; private=46870528->46903296; workingSet=74723328->75317248; tcpEstablished=0->0; tcpTimeWait=0->0; tcpTotal=0->0
defaultSensorSmokeSuiteSoak; durationSeconds=30; maxSeconds=120; elapsedSeconds=30.02265; cycles=1922; scenarioRuns=3844; addValues=0; requests=0; bytes=0
```

```text
adversarialSuiteSoakResources; handles=679->838; threads=29->57; managedGc=4043624->34809320; private=47112192->155394048; workingSet=75677696->182198272; tcpEstablished=0->0; tcpTimeWait=0->0; tcpTotal=0->0
adversarialSuiteSoak; durationSeconds=30; maxSeconds=120; elapsedSeconds=30.9565135; cycles=16; scenarioRuns=160; addValues=432176; sensorCreates=3312; dataFailureBursts=16; commandFailureBursts=32
```

```text
flakyStressSuiteSoakResources; handles=840->1068; threads=57->63; managedGc=34827184->5438824; private=155578368->73297920; workingSet=182468608->100483072; tcpEstablished=0->0; tcpTimeWait=0->3; tcpTotal=0->3
flakyStressSuiteSoak; durationSeconds=30; maxSeconds=120; elapsedSeconds=30.5594716; cycles=10; addValues=192000; requests=825; commands=15; data=810; failures=110; aborts=40; slow=70; bytes=33253204; maxConcurrent=2
```

```text
resourceLeakSuiteSoakResources; handles=1068->1016; threads=63->52; managedGc=5119800->5823000; private=73334784->73433088; workingSet=100524032->100904960; tcpEstablished=0->0; tcpTimeWait=0->0; tcpTotal=0->0
resourceLeakSuiteSoak; durationSeconds=30; maxSeconds=120; elapsedSeconds=32.5019163; suiteCycles=8; resourceCycles=40; addValues=192000; requests=1120; commands=53; data=1067; aborts=120; failures=200; slow=160; bytes=19045314
```

```text
transportSoakTotals; durationSeconds=30; maxSeconds=120; elapsedSeconds=32.2717285; cycles=21; addValues=244000; accepted=1150; requests=1148; commands=1125; data=19; ok=164; dropped=321; hung=171; slowReads=164; headerOnly=166; malformed=166; resets=160; bytes=0; tcpEstablished=0; tcpTimeWait=820; handles=1016->1097; threads=52->64; managedGc=5549528->6086584; private=73469952->75767808; workingSet=100966400->103202816
transportSoakTrendAfterWarmup; handles=1097->1107; threads=64->64; managedGc=6059344->6264712; private=74702848->75763712; workingSet=101957632->103198720
```

## Вывод

- Ни один suite не оставил TCP `ESTABLISHED` соединений к тестовым портам.
- `TIME_WAIT` появился только там, где это ожидаемо: `3` в flaky stress и `820` в raw transport chaos.
- Handles/threads/private bytes/working set остались внутри заданных safety limits.
- Самая высокая транспортная нагрузка сейчас у `Transport chaos`: 1150 accepted TCP connections и 244000 `AddValue()` за один 30-секундный suite.

## Замечания

- `Default sensor smoke` пока не является полноценной проверкой default sensors: тесты выполняются, но assertions внутри них закомментированы.
- `Transport chaos` intentionally имеет `bytes=0` в mixed soak, потому что многие raw TCP сценарии закрывают/ломают соединение до чтения body. Проверка ценна именно как socket/retry/dispose chaos.
- `Resource leak load` и `Flaky server stress` лучше показывают объем реально принятых HTTP body bytes.
