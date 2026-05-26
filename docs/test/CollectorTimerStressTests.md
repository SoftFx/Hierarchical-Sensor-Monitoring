# Collector timer stress tests

Дата прогона: 2026-05-26.

Цель: проверить общий scheduler/timer слой коллектора после перехода на общий `CollectorScheduler`: разные периоды, restart timer-а, отсутствие callback overlap и CPU-budget.

Код тестов:

`src/collector/HSMDataCollector.Tests/CollectorTimerStressTests.cs`

## Что проверяется

| Сценарий | Тест | Что ломаем | Критерии успеха |
| --- | --- | --- | --- |
| Разные timer periods | `Function_sensors_with_varied_timer_periods_fire_without_cpu_spin` | 7 function sensors с периодами `40/60/90/130/200/300/500 ms` работают одновременно | каждый timer сработал; быстрый timer дал больше callbacks, чем медленный; data packages дошли до sender; CPU за окно меньше `4 sec` |
| Restart timer под нагрузкой | `Restarting_function_timer_under_load_changes_rate_without_callback_overlap` | function callback спит `40 ms`, timer перезапускается с `100 ms` на `25 ms` | после restart callback rate вырос; `maxConcurrent=1`; CPU за окно меньше `4 sec` |
| Break candidate: blocked callback | `Exploratory_blocked_function_timer_callback_does_not_block_collector_stop` | function callback зависает и не возвращает управление | сейчас воспроизводит проблему: `Collector.Stop()` не завершается за `2 sec`; обычный прогон пропускает тест |

## Локальный результат

Команда:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorTimerStressTests" --logger "console;verbosity=detailed"
```

Фактический вывод из общего targeted run:

```text
timerStress; scenario=varied-periods; callbacks=40ms=51,60ms=34,90ms=23,130ms=16,200ms=11,300ms=7,500ms=5; dataPackages=54; dataValues=144; wallMs=2010; cpuMs=234; cpuCores=0.117
```

```text
timerRestart; beforeRestartCallbacks=7; afterRestartCallbacks=15; maxConcurrent=1; dataPackages=21; dataValues=21; wallMs=902; cpuMs=0; cpuCores=0.000
```

## Вывод

- Таймеры с разными периодами срабатывают в ожидаемом порядке: `40 ms` чаще, чем `500 ms`.
- Restart на более короткий период реально увеличивает callback rate.
- Callback-и не накладываются друг на друга даже когда выполнение (`40 ms`) дольше нового периода (`25 ms`).
- CPU в timer stress окнах остается низким.
- Найден break candidate: зависший пользовательский function callback блокирует `Collector.Stop()`. Подробно: [CollectorBreakCandidates.md](CollectorBreakCandidates.md).
