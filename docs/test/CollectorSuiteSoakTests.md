# Collector suite soak tests

Дата прогона: 2026-05-26.

Цель: прогнать каждую группу тестов не один раз, а повторять весь suite по кругу в течение заданного времени. На текущий момент default duration - `30` секунд на suite.

Тесты выполняются последовательно. Для этого test assembly отключает параллельное выполнение через `TestAssemblyInfo.cs`, чтобы TCP/resource метрики разных suite не смешивались.

## Команда запуска

```powershell
$env:HSM_COLLECTOR_RUN_SUITE_SOAK="1"
$env:HSM_COLLECTOR_SUITE_SOAK_SECONDS="30"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~suite_repeated" --logger "console;verbosity=detailed"
```

## Итог

```text
Total tests: 5
Passed: 5
Failed: 0
Total time: 2.6519 minutes
```

## Результаты по suite

| Suite | Тест | Длительность | Сколько успел пройти | Основные счетчики | Результат |
| --- | --- | ---: | ---: | --- | --- |
| Flaky server stress | `Flaky_server_stress_suite_repeated_for_duration_stays_green` | 30 sec | 10 cycles | примерно 82-83 HTTP requests на цикл; HTTP 500, abort и slow responses в каждом цикле | Passed |
| Resource leaks | `Resource_leak_suite_repeated_for_duration_stays_bounded` | 30 sec | 8 suite cycles / 40 resource cycles | примерно 1120 HTTP requests; `tcpEstablished=0` в каждом resource cycle | Passed |
| Adversarial lifecycle | `Adversarial_suite_repeated_for_duration_stays_green` | 30 sec | 16 suite cycles / 160 scenario runs | все 10 adversarial сценариев повторены 16 раз | Passed |
| Transport chaos | `Mixed_transport_chaos_suite_repeated_on_one_server_stays_bounded` | 30 sec | 20 mixed cycles | `accepted=1093`, `requests=1091`, `tcpEstablished=0`, `tcpTimeWait=734` | Passed |
| Default sensor smoke | `Default_sensor_smoke_suite_repeated_for_duration` | 30 sec | 1928 suite cycles / 3856 scenario runs | smoke-only; текущие assertions закомментированы | Passed |

## Transport Resource Trend

Transport suite был самым важным для вопроса про socket leak. Итоговые счетчики:

```text
accepted=1093
requests=1091
dropped=303
hung=161
slowReads=157
headerOnly=154
malformed=157
resets=159
tcpEstablished=0
tcpTimeWait=734
```

Тренд после warm-up:

```text
handles: 1095 -> 1091
threads: 68 -> 66
managedGc: 5100336 -> 5619816
private: 71909376 -> 71331840
workingSet: 98070528 -> 97607680
```

Вывод: на 30-секундном mixed transport soak сокеты не остались в `ESTABLISHED`, `TIME_WAIT` остался ниже текущего лимита `1000`, handles/threads после warm-up не растут.

## Замечания

- `Default sensor smoke` пока не является полноценной проверкой default sensors: тесты выполняются, но assertions внутри них закомментированы.
- `Resource leaks` и `Transport chaos` дают наиболее полезные ресурсные сигналы.
- `Flaky server stress` показывает устойчивость под HTTP 500, abort и slow responses, но это не socket-level raw TCP chaos.
