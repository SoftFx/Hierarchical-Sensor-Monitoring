# Сопроводительное письмо: подключение HSMCppWrapper к аггрегатору

Привет!

Передаю нативную C++-библиотеку мониторинга **HSMCppWrapper** для встраивания в аггрегатор.
Ниже — что это, что взять, как собрать, как пользоваться и на что обратить внимание. Всё
подробное лежит в самом бандле и в репозитории (ссылки в конце) — это письмо, чтобы
сориентироваться за 10 минут и начать.

---

## 1. Что это, в двух словах

`HSMCppWrapper.dll` — **чисто нативная** (без CLR / .NET) C++-библиотека, которая шлёт метрики на
сервер HSM по HTTP(S). Это замена старой C++/CLI-обёртки: **публичный ABI не изменился** — те же
заголовки, те же классы (`hsm_wrapper::DataCollectorProxy`, сенсоры, alert-DSL). Под капотом теперь
нативное ядро `hsm::collector` + libcurl (TLS через schannel).

- Платформа: **Windows x64**, MSVC, C++17.
- Транспорт: libcurl, поставляется рядом с DLL (никакого .NET-рантайма не грузится).
- Для существующего кода аггрегатора это **пересборка (relink), без правок исходников**.

---

## 2. Что вы получаете (бандл)

Пребилд публикуется как **GitHub Release с тегом `wrapper-v<версия>`**, один zip. Раскладка
повторяет ваше вендорное дерево — распаковывается «поверх» чекаута:

```
HSMCppWrapper-<ver>/
  include/HSMCppWrapper/   публичные заголовки ABI  (вариант A — relink)
  include/hsm_collector/   заголовки нативного ядра (для Native() и варианта B)
  dll/HSMCppWrapper/x64/{Release,Debug}/   HSMCppWrapper.dll + .pdb + рантайм libcurl/zlib
  lib/HSMCppWrapper/x64/{Release,Debug}/   HSMCppWrapper.lib        (вариант A)
  lib/hsm_collector/x64/{Release,Debug}/   hsm_collector_core.lib   (вариант B)
  MANIFEST.md   ← точная версия, исходный commit и оба рецепта подключения (A / B)
```

> **`MANIFEST.md` внутри бандла — главный документ.** Там записан точный commit сборки и пошаговый
> рецепт замены. Точные имена рантайм-DLL (libcurl + zlib) тоже смотрите в нём — они зависят от
> сборки vcpkg, поэтому здесь я их не фиксирую.

---

## 3. Быстрый старт — вариант A (relink, рекомендуемый)

Оставляете API `DataCollectorProxy` как есть, просто пересобираетесь на новый DLL:

1. Скопировать `include/`, `dll/`, `lib/` из бандла поверх ваших вендорных папок.
2. Положить рантайм-DLL (`libcurl…` + `zlib…`) рядом с исполняемым файлом (обычно ваш PostBuild
   xcopy `dll/**/*.dll → OutDir` уже это делает).
3. **Удалить managed-хвосты** из выходной/вендорной папки — они больше не грузятся:
   `HSMDataCollector.dll`, `HSMSensorDataObjects.dll` (и их `.pdb`).
4. Пересобрать. CLR больше не поднимается. Экспортируемый ABI совпадает (проверено `dumpbin
   /LINKERMEMBER`), поэтому линковка проходит без единой правки исходников.

Полный рецепт (что именно перезаписать/удалить) — в `MANIFEST.md` бандла.

---

## 4. Как пользоваться (API)

Точка входа — `hsm_wrapper::DataCollectorProxy`. Конструктор: `(product_key, address, port, module)`,
где `product_key` — ключ доступа продукта на сервере HSM (GUID), `port` — порт Sensor API (по
умолчанию **44330**), `module` — имя модуля/узла в дереве.

Минимальный сценарий:

```cpp
#include "HSMCppWrapper.h"
using namespace hsm_wrapper;

DataCollectorProxy collector("<product-key-guid>", "hsm.host", 44330, "Aggregator");
collector.Initialize();                      // конфиг задаётся конструктором (см. п.5)

collector.InitializeSystemMonitoring();      // готовые группы системных сенсоров
collector.InitializeProcessMonitoring();
collector.InitializeProductVersion("1.2.3.4");

auto load = collector.CreateDoubleSensor("Aggregator/load", "средняя нагрузка");

// Алерты через fluent-DSL:
HSMInstantSensorOptions opts;
opts.alerts.push_back(
    AlertsBuilder::IfValue(HSMAlertOperation::GreaterThan, 100)
        .ThenSendNotification("слишком много: $value")
        .AndSetSensorError()
        .Build());
auto guarded = collector.CreateIntSensor("Aggregator/guarded", opts);

collector.StartAsync();                       // или Start()
load.AddValue(3.14);
guarded.AddValue(150);
// ... работа ...
collector.Stop();                             // или StopAsync()
```

> **Полный рабочий пример со ВСЕМИ типами сенсоров** (bool/int/double/string, bar, rate,
> last-value, values-function, файл, service-state, Native()) — это `WrapperSmoke.cpp` в репозитории
> (`src/wrapper/WrapperConsole/WrapperSmoke.cpp`). Он гоняет ровно тот публичный ABI, который
> дёргает аггрегатор. Берите его за референс.

**Обработка ошибок:** любой сбойный вызов бросает исключение (`std::exception` / внутри —
`hsm::collector::Error`). Оборачивайте инициализацию/создание сенсоров в `try/catch`.

**Инкрементальная миграция (по желанию):** `collector.Native()` возвращает ссылку на нативный
`hsm::collector::Collector` на том же соединении — новые сенсоры можно писать сразу на нативном API
(`Native().CreateDoubleSensor(...)`), не трогая старые. Для этого подключите
`#include "hsm_collector/hsm_collector.hpp"`.

---

## 5. Что важно знать (поведенческие отличия relink-пути)

ABI прежний, но несколько поведений идут от нативного ядра, а не от старой managed-обёртки. Для
аггрегатора это в основном no-op (вы и так шлёте значения по умолчанию), но чтобы не удивляться:

- **Булевы под-флаги `Initialize*Monitoring(...)` игнорируются** — регистрируется вся штатная группа
  сенсоров; отдельные под-сенсоры не выключить.
- **`InitializeDiskMonitoring(target, …)` игнорирует `target`** — это теперь эквивалент
  `InitializeAllDisksMonitoring` (весь дефолтный набор дисков, без выбора конкретного тома).
- **`Initialize(config_path, write_debug)` — no-op.** Конфигурация задаётся конструктором, файла
  конфига/CLR-бутстрапа больше нет. Вызов можно оставить (он в ABI), он просто ничего не делает.
- **`SendFileAsync` синхронный** — читает файл в вызывающем потоке (несмотря на «Async»); лимит 10 MiB,
  UTF-8-текст.
- **Функциональные сенсоры — только int.** `CreateNoParamsFuncSensor<T>` / `CreateParamsFuncSensor<T,U>`
  бросают при создании, если `T` (и `U`) не `int`. (Аггрегатор использует `<int,int>` — это ок.)
- **Int-rate и time-in-GC-сенсоры убраны** (rate теперь double-only, GC в нативном хосте нет).

Полная таблица соответствий «старая обёртка → нативка» и статус каждого метода — в
`docs/native-collector-migration.md` (раздел *Quick comparison* и *Native wrapper DLL — behavioral
notes*). Это самый подробный источник по поведению.

---

## 6. Если хотите без обёртки (вариант B — нативный адаптер)

Можно писать прямо на нативном `hsm::collector` и вообще выкинуть `HSMCppWrapper.dll`.

**Рекомендуемый путь — vcpkg.** Этот репозиторий сам является vcpkg-реестром, поэтому ядро ставится
как обычный порт, а libcurl приезжает зависимостью — из бандла для этого варианта брать уже ничего
не нужно. `vcpkg-configuration.json` рядом с вашим `vcpkg.json`:

```json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/SoftFx/Hierarchical-Sensor-Monitoring",
      "baseline": "<commit master>",
      "packages": ["hsm-collector"]
    }
  ]
}
```

Дальше `"dependencies": [{ "name": "hsm-collector", "features": ["http"] }]`, сборка с vcpkg-тулчейном
и `find_package(hsm_collector CONFIG REQUIRED)` + `target_link_libraries(... hsm_collector::hsm_collector)`.
Фича `http` — это транспорт на libcurl (schannel на Windows); без неё ядро собирается без сети.

**Путь из бандла (если vcpkg в проекте не используется).** В zip лежит `hsm_collector_core.lib`:

- Заголовки: `#include "hsm_collector/hsm_collector.hpp"` (RAII-C++ API поверх C-ABI из
  `hsm_collector.h`).
- Линковка (по конфигу): `hsm_collector_core.lib` + `iphlpapi.lib` + `pdh.lib` + `libcurl.lib`.
- Рядом с бинарём кладёте тот же рантайм `libcurl…` + `zlib…`.

Плюс варианта B: новый API (enum-сенсоры, lifecycle-listeners, `find_package(hsm_collector)`). Минус:
спеллинг методов отличается — см. ту же таблицу в `docs/native-collector-migration.md`. Рецепт из
бандла есть в `MANIFEST.md`.

---

## 7. Сборка бандла своими руками (если нужен свежий билд)

```powershell
vcpkg install curl:x64-windows
cmake -S src/wrapper -B build/wrapper `
  -DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_INSTALLATION_ROOT/scripts/buildsystems/vcpkg.cmake `
  -DVCPKG_TARGET_TRIPLET=x64-windows
cmake --build build/wrapper --config Release
cmake --build build/wrapper --config Debug
pwsh src/wrapper/packaging/pack.ps1 -BuildDir build/wrapper -Version 1.0.0
#  → dist/HSMCppWrapper-1.0.0.zip
```

Инструкция по сборке/выпуску — `src/wrapper/packaging/README.md`.

---

## 8. Версии и обновления

- Новый пребилд = тег **`wrapper-v<major>.<minor>.<patch>`** (можно pre-release суффикс, напр.
  `wrapper-v1.0.0-rc1`; `+build`-метаданные в теге **не** принимаются). Пуш тега → CI собирает оба
  конфига, гоняет ABI-smoke и публикует Release с zip.
- Версия обёртки живёт **независимо** от версии сервера и от C-ABI нативного ядра
  (`HSM_COLLECTOR_VERSION`). В `MANIFEST.md` каждого бандла записан точный исходный commit.
- За новой версией — просто скачиваете свежий Release-ассет и повторяете рецепт из п.3.

---

## 9. Куда смотреть (авторитетные источники)

| Что | Где |
|---|---|
| Точный рецепт замены + commit сборки | `MANIFEST.md` **внутри бандла** |
| Рабочий пример на весь ABI | `src/wrapper/WrapperConsole/WrapperSmoke.cpp` |
| Поведение / таблица соответствий | `docs/native-collector-migration.md` |
| Сборка и выпуск бандла | `src/wrapper/packaging/README.md` |
| Публичные заголовки | `src/wrapper/include/` (зонтичный — `HSMCppWrapper.h`) |
| Нативное ядро через vcpkg (вариант B) | вики: [C++ Data Collector](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Cpp-Data-Collector); версии порта — `ports/hsm-collector/` и `versions/baseline.json` |

Если что-то не линкуется, не стартует или ведёт себя не как ожидалось — пишите, разберёмся.
Удобнее всего приложить: версию бандла (из `MANIFEST.md`), конфиг (Release/Debug) и текст ошибки
линковки/исключения.

Спасибо!
