# Home page — design requirements

Дизайн-спецификация и руководство для разработчика по главной странице приложения (`Views/Home/Index.cshtml` + layout `Views/Tree/_Layout.cshtml`). Это самая сложная страница продукта: левый сайдбар с деревом сенсоров и правая панель с детальной информацией по выбранному узлу.

Документ продолжает стилевую систему из `auth-redesign.md` и `products-redesign.md`.

---

## 1. Цели и принципы

1. Снизить визуальный шум: меньше границ, спокойные иконки, единый ритм отступов. Сейчас на странице одновременно работают bootstrap-аккордеоны, jsTree, плотные таблицы, плюс множество inline-стилей — нужно унифицировать.
2. Сделать дерево читаемым: статус-иконки разнесены по столбикам, а не сплавлены в одну строку.
3. Дать сенсорному header'у роль "паспорта" — имя, статус, описание, активные алерты — без UI-чипов, плоским текстом.
4. Структура табов: **Graph · Table · Journal · Alerts · Settings**. На табах с временным контекстом — period-селектор сразу под рядом табов.
5. Алерты в header — read-only текст, чтобы пользователь видел список одним взглядом. Полноценная конфигурация — на табе Alerts.
6. Сохранить весь текущий функционал (polling, jsTree, журналы, плот-чарты) — меняется только подача.

Принципы, общие для проекта:

- Только два веса шрифта: 400, 500.
- Sentence case везде.
- Бордеры — `0.5px solid var(--hsm-border-tertiary)`.
- Радиусы — 8 px (md) для кнопок/инпутов, 12 px (lg) для карточек.
- Иконки — FontAwesome 6.2.1.
- Никаких теней, кроме фокус-кольца.

---

## 2. Анатомия страницы

```
+-------------------------------------------------------------+
|  Top nav (общий, из _Layout.cshtml)                         |
+--------------+----------------------------------------------+
|              |  Header сенсора                              |
|  Tree        |  ─────────────────────────────────────       |
|  sidebar     |  [ Graph | Table | Journal | Alerts | … ]    |
|              |  Period selector  (если применимо)           |
|              |  ─────────────────────────────────────       |
|              |  Контент таба                                |
|              |                                              |
+--------------+----------------------------------------------+
```

- **Сайдбар** — фиксированная ширина 320 px (resizable handle опционально). Скроллится независимо от правой панели.
- **Header сенсора** — sticky внутри правой панели; при скролле остаётся видимым.
- **Tabs row** — sticky под header'ом.
- **Period selector** — sticky под tabs row (только для табов Graph / Table / Journal).
- **Контент таба** — скроллится.

Если ничего не выбрано — правая панель показывает empty state: иконка `fa-folder-tree`, заголовок "Pick a sensor or folder", подпись "Select an item in the tree to see details".

---

## 3. Дизайн-токены

Берутся из `wwwroot/src/css/theme.css`:

```
Цвета:
  --hsm-bg-page         — фон страницы
  --hsm-surface          — белый фон карточек
  --hsm-surface-muted    — мягкий светлый (#FAFAF7), thead, выбранный узел в дереве
  --hsm-border-tertiary  — 0.5px бордеры по умолчанию
  --hsm-border-secondary — 0.5px бордеры на эмфазе
  --hsm-text-primary
  --hsm-text-secondary
  --hsm-text-tertiary
  --hsm-accent           — #378ADD
  --hsm-accent-bg        — #E6F1FB
  --hsm-accent-dark      — #185FA5

Статусные:
  --hsm-status-ok        — #639922
  --hsm-status-error     — #E24B4A
  --hsm-status-warn      — #BA7517 (= --hsm-warn)
  --hsm-status-offtime   — #888780
  --hsm-status-unconf    — #D4537E

Радиусы и отступы:
  --hsm-radius-md = 8px
  --hsm-radius-lg = 12px
  --hsm-space-1 = 4px
  --hsm-space-2 = 8px
  --hsm-space-3 = 12px
  --hsm-space-4 = 16px
  --hsm-space-5 = 20px
  --hsm-space-6 = 24px

Типографика:
  h2 (имя сенсора)            18px / 500
  Хлебные крошки               12px / 400, --hsm-text-secondary
  Section subtitle (alerts)    13px / 500, --hsm-text-secondary
  Tab label                    13px / 500
  Body                          13px / 400
  Caption                       12px / 400, --hsm-text-secondary
  Tree node                    13px / 400 (selected: 500)
  Mono (значения, ID)         13px / 400, --font-mono
```

---

## 4. Компоненты в деталях

### 4.1. Tree sidebar

```
+----------------------------------------+
|  🔍 Filter sensors            ⚙  ⤴    | — toolbar
|----------------------------------------|
|  ▾ Production                          |
|    ▾ Sensor Lab                        |
|      ▾ Power                           |
|        ● voltage           42.1 V  ⚠   |
|        ● current           1.7  A      |
|        ● temperature       —           |
|      ▸ Network                         |
|    ▸ Edge Gateway                      |
|  ▸ Staging                             |
+----------------------------------------+
```

#### Toolbar (фиксирован сверху сайдбара)

- Высота 40 px, `background: var(--hsm-surface); border-bottom: 0.5px solid var(--hsm-border-tertiary)`.
- Слева — search input с иконкой `fa-magnifying-glass`. Высота 28 px, font 13 px, `flex: 1`. На фокус — `border: 0.5px solid var(--hsm-accent); box-shadow: 0 0 0 3px rgba(55,138,221,0.15)`.
- Справа — кнопка-иконка фильтра `fa-sliders` (открывает существующий `_TreeFilter.cshtml` как dropdown), и `fa-arrows-rotate` (refresh). Кнопки 28×28 px, без бордера, hover `background: var(--hsm-surface-muted)`.

Если активны фильтры (например, `HasOkStatus = false`) — на кнопке фильтра показать blue dot (`8×8 px`, `--hsm-accent`, position absolute top-right).

#### Tree (jsTree)

- Используется существующий jsTree, кастомизация — через CSS-override.
- Каждая нода — строка `min-height: 28px`, padding `2px 8px 2px 4px`, font 13 px.
- Выбранный узел: `background: var(--hsm-surface-muted)`, текст `--hsm-text-primary` 500.
- Hover: `background: rgba(0,0,0,0.03)`.
- Узел — `caret + status dot + name + (optional) right-side info`. Каретка `fa-caret-right` / `fa-caret-down`, размер 12 px, цвет `--hsm-text-tertiary`.
- Статус-иконка — `fa-circle` 8 px со статусным цветом (см. дальше). Сейчас в коде это `<i class="fas fa-circle tree-icon-ok|error|offTime|warning">` — оставляем класс, перекрашиваем через CSS-переменные.
- Справа от имени — последнее значение сенсора (если включено в фильтре `IsSensorsCountVisible` / `AreIconsVisible`) — мелким моно-шрифтом 12 px, `--hsm-text-secondary`, выровнено по правому краю.
- Иконки атрибутов сенсора (muted, unconfigured-alerts, grafana) — после имени, 12 px, серым `--hsm-text-tertiary`, с тултипом.

#### Цвета статусов в дереве

| Статус         | CSS-класс                    | Цвет                 |
|----------------|-----------------------------|----------------------|
| Ok             | `.tree-icon-ok`              | `--hsm-status-ok`     |
| Error          | `.tree-icon-error`           | `--hsm-status-error`  |
| OffTime        | `.tree-icon-offTime`         | `--hsm-status-offtime`|
| Warning        | `.tree-icon-warning`         | `--hsm-status-warn`   |

Для unconfigured alerts — отдельная иконка `fa-comment-slash` цветом `--hsm-status-unconf`.

### 4.2. Header сенсора (правая панель сверху)

Sticky, `padding: 16px 24px`, `background: var(--hsm-surface)`, `border-bottom: 0.5px solid var(--hsm-border-tertiary)`.

```
Production / Sensor Lab / Power / voltage           ⋯ Edit  ⋯ More
voltage                                       ● Ok · 42.1 V
Reads line voltage from the main PSU. Critical for fail-over detection.
Alerts:
  When value > 50 V, send to TG @ops-critical
  When value < 10 V, send to TG @ops-warn, mute sensor
```

Структура сверху вниз:

1. **Breadcrumb** — путь от корня. 12 px, `--hsm-text-secondary`. Разделитель — `·` или `/` без жирного. Каждый сегмент — ссылка (выбирает узел в дереве). Текущий узел — без ссылки, `--hsm-text-primary` 500.
2. **Sensor row** — display flex, justify-between, gap 16 px, margin-top 6 px.
   - Слева — имя сенсора `h2`, 18 px / 500.
   - Справа — inline-блок со статус-дотом `8×8 px` цвета статуса, словесным статусом ("Ok" / "Error" / "OffTime") 13 px / 500 цветом статуса, разделитель `·`, последнее значение моно-шрифтом 13 px / 500.
3. **Description** — markdown, рендерится через существующий `replaceHtmlToMarkdown`. Контейнер: 13 px, `--hsm-text-secondary`, `line-height: 1.5`, `margin-top: 8px`, `max-width: 720px`. Если описание пустое — строка не рендерится.
4. **Alerts list** — секция:
   - Subtitle "Alerts:" 13 px / 500, `--hsm-text-secondary`, `margin-top: 12px`.
   - Дальше — каждый алерт отдельной строкой 13 px / 400, `--hsm-text-primary`, `padding-left: 12px`, читаемый человеком текст. Никаких чипов и кнопок: чистая информация, как сейчас в текстовом представлении.
   - Формат строки: `When <condition>, <action>[; <action>]`. Текст уже формируется в `Alerts/_DataAlert.cshtml` через `alert.ToString(ChatsManager)` — используем его.
   - Если алертов нет — строка "No alerts configured" 13 px, `--hsm-text-tertiary`.

Справа в header'е — выпадающее меню действий уровня сенсора (Mute / Unmute, Edit, Remove, Export alerts, Import alerts) под кнопкой `fa-ellipsis-vertical` 32×32 px.

### 4.3. Tabs row

```
[ Graph ] [ Table ] [ Journal ] [ Alerts ] [ Settings ]
```

Sticky, `padding: 0 24px`, `background: var(--hsm-surface)`, `border-bottom: 0.5px solid var(--hsm-border-tertiary)`. Высота 40 px.

Каждый таб — кнопка-пилюля:

- Padding `8px 14px`, font 13 px / 500, color `--hsm-text-secondary`.
- Hover: `background: var(--hsm-surface-muted)`.
- Активный таб: `color: var(--hsm-text-primary)`, нижняя полоска `border-bottom: 2px solid var(--hsm-accent)`, padding-bottom уменьшается на 2 px чтобы скомпенсировать.

Слева иконка (опционально):
- Graph — `fa-chart-line`
- Table — `fa-table`
- Journal — `fa-clock-rotate-left`
- Alerts — `fa-bell`
- Settings — `fa-sliders`

Видимость табов зависит от типа узла:

| Тип узла              | Доступные табы                                |
|----------------------|-----------------------------------------------|
| Sensor (numeric)      | Graph, Table, Journal, Alerts, Settings       |
| Sensor (file)         | Files, Journal, Alerts, Settings              |
| Sensor (bool/string)  | Table, Journal, Alerts, Settings              |
| Folder / Product / Module | Children, Journal, Settings              |

Если таб недоступен — он не рендерится.

### 4.4. Period selector

```
+------------------------------------------------------+
|  Last 1 hour ▾   from 2026-05-25 12:00 to now   Apply|
+------------------------------------------------------+
```

Sticky под tabs row. `background: var(--hsm-accent-bg)` (#E6F1FB), `border: 0.5px solid #B5D4F4`, `border-radius: var(--hsm-radius-md)`, `margin: 12px 24px 0`, `padding: 8px 12px`. Высота ~40 px. Делается выраженным, потому что период — основной фильтр для Graph/Table.

Внутри:

- **Quick-select dropdown** слева. Опции: Last 30 min, Last 1 hour, Last 6 hours, Last 24 hours, Last 7 days, Custom. Высота 28 px, font 13 px, border `0.5px solid #B5D4F4`, background white, радиус 6 px.
- При выборе Custom — справа открывается двух-инпутный datepicker (используется текущий flatpickr): `from … to …`. До этого момента показывается компактная сводка "from <X> to now".
- Справа кнопка **Apply** — primary стиль, высота 28 px, padding `0 14px`. Активируется только при изменении значения.

Period отображается только на табах Graph / Table / Journal. На Alerts / Settings — скрыт.

### 4.5. Tab content — общая структура

Контент каждого таба обёрнут в `padding: 16px 24px`, скроллится отдельно от sticky-зоны.

#### Graph tab

- На всю доступную ширину — Plotly-чарт (`div#chart`), `min-height: 420px`.
- Над чартом — тулбар: справа inline-кнопки `fa-download` (Export CSV), `fa-arrow-rotate-right` (Reload), `fa-image` (Save PNG). Высота 28 px, без бордера, hover `background: var(--hsm-surface-muted)`.
- Под чартом — мелкая legend-строка, 12 px, `--hsm-text-secondary`: "Points: 1,243 · Range: 12:00 – 13:00 · Step: 1 min".

#### Table tab

- Таблица истории сенсора в две колонки (Time, Value) или больше, в зависимости от типа.
- Шапка `background: var(--hsm-surface-muted)`, padding `8 16`, font 12 px / 500, `--hsm-text-secondary`.
- Строки: padding `8 16`, border-top 0.5px, hover светлый.
- Пагинация снизу — компактный блок: `‹ 1 2 3 4 5 ›`, кнопки 28×28 px, активная — `background: var(--hsm-accent); color: white`. Справа — селектор Page size (25/50/100).
- Поверх таблицы, если в реальном времени пришли новые значения — узкая желтая полоска под шапкой с текстом "5 new values · Refresh" → клик `tableHistoryRefreshButton`. Цвет фон `#FAEEDA`, текст `#854F0B`, font 12 px.

#### Journal tab

- Список событий journal'а: чат-стиль или табличный, на выбор.
- Каждая строка — `padding: 10px 16px`, `border-top: 0.5px solid var(--hsm-border-tertiary)`.
- Слева — иконка типа события (`fa-circle-info` для info, `fa-triangle-exclamation` для warning, `fa-circle-exclamation` для error) с цветами `--hsm-text-secondary` / `--hsm-status-warn` / `--hsm-status-error`.
- В центре — текст события, 13 px.
- Справа — relative time, 12 px, `--hsm-text-secondary`.
- Внизу пагинация как в Table.

#### Alerts tab

Полный rule-builder для алертов. Структура:

- Заголовок секции: "Alert rules" + справа кнопка `+ Add rule` (primary стиль).
- Каждая правила — карточка `background: var(--hsm-surface); border: 0.5px solid var(--hsm-border-tertiary); border-radius: var(--hsm-radius-lg); padding: 16px; margin-bottom: 12px`.
- Внутри карточки:
  - Чип-конструктор: "When [property] [operator] [value], [action]; [action]".
  - Каждый чип — `display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; background: var(--hsm-accent-bg); color: var(--hsm-accent-dark); border-radius: 6px; font-size: 13px; font-weight: 500; cursor: pointer`.
  - На клик чипа — открывается inline-редактор (dropdown / popover) с возможными значениями.
  - Кнопка "+ Add action" чипом, тип `background: transparent; border: 0.5px dashed var(--hsm-border-secondary); color: var(--hsm-text-secondary)`.
- В правом верхнем углу карточки — кнопка `fa-ellipsis-vertical` с пунктами Edit / Duplicate / Remove.

Empty state: иконка `fa-bell-slash`, текст "No alert rules", primary-кнопка "Create your first rule".

#### Settings tab

Длинная форма, разделена на секции с заголовками 13 px / 500:

- **General info**: Name, Description (markdown editor), Unit, Aggregate type, Type — поля `<input>` и `<textarea>` в стиле products-search inputs (32 px высота).
- **Cleanup**: Self-destroy interval, TTL.
- **Сохранение**: sticky-bar снизу с кнопками Cancel / Save (primary). Появляется только когда форма "грязная".

### 4.6. Empty state (узел не выбран)

В правой панели по центру, padding `60px 20px`, display flex column align-center:

- Иконка `fa-folder-tree` 40 px, `--hsm-text-tertiary`.
- Заголовок 16 px / 500 "Pick a sensor or folder".
- Подпись 13 px, `--hsm-text-secondary` "Select an item in the tree on the left to see its details and history".

---

## 5. Типы узлов и состояния

### Статусы сенсора

| Статус       | Цвет точки         | Тег цветом       | Описание                              |
|--------------|--------------------|------------------|---------------------------------------|
| Ok           | `--hsm-status-ok`   | `--hsm-status-ok` | Всё штатно                             |
| Error        | `--hsm-status-error`| `--hsm-status-error` | Сработало правило / валидация       |
| OffTime      | `--hsm-status-offtime` | `--hsm-status-offtime` | Сенсор не присылает данные      |
| Warning      | `--hsm-status-warn` | `--hsm-status-warn` | Предупреждение от правила             |
| Unconfigured | `--hsm-status-unconf` | `--hsm-status-unconf` | Алерты есть, но не сконфигурированы чаты |

### Состояния header'а

- Sensor muted — рядом с именем sensor'а добавить иконку `fa-volume-xmark` 14 px, `--hsm-text-tertiary`, tooltip "Muted until 14:30".
- Validation error — под description'ом show баннер `background: #FCEBEB; border: 0.5px solid #F09595; padding: 8 12; border-radius: var(--hsm-radius-md)`. Внутри `fa-triangle-exclamation` + текст ошибки.

---

## 6. FontAwesome icon mapping

| В мокапе (Tabler)    | В коде (FontAwesome)             | Где                                    |
|---------------------|---------------------------------|----------------------------------------|
| ti-search           | fa-magnifying-glass             | Tree search                            |
| ti-adjustments      | fa-sliders                      | Tree filter / Settings tab             |
| ti-refresh          | fa-arrows-rotate                | Tree refresh, Graph reload             |
| ti-caret-right/down | fa-caret-right / fa-caret-down  | Tree expand/collapse                   |
| ti-circle-filled    | fa-circle                       | Status dot                             |
| ti-volume-3 off     | fa-volume-xmark                 | Muted sensor                           |
| ti-message-off      | fa-comment-slash                | Unconfigured alerts                    |
| ti-alert-triangle   | fa-triangle-exclamation         | Warning, validation error              |
| ti-alert-circle     | fa-circle-exclamation           | Error journal entry                    |
| ti-info-circle      | fa-circle-info                  | Info journal entry                     |
| ti-chart-line       | fa-chart-line                   | Graph tab                              |
| ti-table            | fa-table                        | Table tab                              |
| ti-history          | fa-clock-rotate-left            | Journal tab                            |
| ti-bell             | fa-bell                         | Alerts tab                             |
| ti-bell-off         | fa-bell-slash                   | Empty alerts state                     |
| ti-folder-tree      | fa-folder-tree                  | Empty home state                       |
| ti-download         | fa-download                     | Export CSV                             |
| ti-photo            | fa-image                        | Save PNG                               |
| ti-dots-vertical    | fa-ellipsis-vertical            | Actions menu                           |

---

## 7. Razor markup — рекомендации

Основная страница (`Views/Home/Index.cshtml`) сохраняет структуру: правая панель рендерится в `#nodeDataPanel` через partial `_NodeDataPanel`. Меняется внутреннее строение partial'ов.

`_NodeDataPanel.cshtml` — каркас правой панели:

```cshtml
@model NodeViewModel

@if (Model is null)
{
    @await Html.PartialAsync("_EmptyState")
    return;
}

<div class="sensor-panel">
    @await Html.PartialAsync("_SensorHeader", Model)
    @await Html.PartialAsync("_TabsRow", Model)
    @await Html.PartialAsync("_PeriodBar", Model)

    <div class="sensor-panel__content" id="sensorTabContent">
        @* render одного из табов по умолчанию (Graph для numeric, etc.) *@
    </div>
</div>
```

`_SensorHeader.cshtml` (новый файл):

```cshtml
@model NodeViewModel

<header class="sensor-header">
    <nav class="sensor-header__crumbs">
        @foreach (var (name, id, isLast) in Model.BreadcrumbPath)
        {
            if (isLast) { <span class="current">@name</span> }
            else { <a data-node-id="@id">@name</a><span class="sep">·</span> }
        }
    </nav>

    <div class="sensor-header__main">
        <h2 class="sensor-header__name">
            @Model.Name
            @if (Model.IsMuted) { <i class="fa-solid fa-volume-xmark muted-icon" title="Muted"></i> }
        </h2>
        @if (Model is SensorNodeViewModel sensor)
        {
            <div class="sensor-header__status">
                <span class="status-dot status-@sensor.Status.ToString().ToLower()"></span>
                <span class="status-label status-@sensor.Status.ToString().ToLower()">@sensor.Status</span>
                <span class="sep">·</span>
                <span class="sensor-value">@sensor.LastValue</span>
            </div>
        }
        <button class="sensor-header__menu" aria-label="Actions"><i class="fa-solid fa-ellipsis-vertical"></i></button>
    </div>

    @if (!string.IsNullOrWhiteSpace(Model.Description))
    {
        <div id="markdown_description" class="sensor-header__description">@Model.Description</div>
    }

    @await Html.PartialAsync("_SensorHeaderAlerts", Model)
</header>
```

`_SensorHeaderAlerts.cshtml` (новый partial для plaintext-списка алертов):

```cshtml
@model NodeViewModel
@inject ITelegramChatsManager Chats

@{
    var alerts = Model.GetActiveAlerts(); // method on view model
}

@if (alerts?.Count > 0)
{
    <div class="sensor-header__alerts">
        <div class="sensor-header__alerts-title">Alerts:</div>
        @foreach (var alert in alerts)
        {
            <div class="sensor-header__alert-line">@alert.ToString(Chats)</div>
        }
    </div>
}
else
{
    <div class="sensor-header__alerts-empty">No alerts configured</div>
}
```

`_TabsRow.cshtml`:

```cshtml
@model NodeViewModel

<nav class="sensor-tabs" role="tablist">
    @foreach (var tab in Model.AvailableTabs)
    {
        <button class="sensor-tab @(tab.IsActive ? "is-active" : "")"
                data-tab="@tab.Key" role="tab" aria-selected="@tab.IsActive">
            <i class="fa-solid @tab.IconClass" aria-hidden="true"></i>
            @tab.Label
        </button>
    }
</nav>
```

`_PeriodBar.cshtml`:

```cshtml
@model NodeViewModel

@if (Model.IsTimeContextAvailable)
{
    <div class="period-bar">
        <select id="periodQuick" class="period-bar__quick">
            <option value="30m">Last 30 min</option>
            <option value="1h" selected>Last 1 hour</option>
            <option value="6h">Last 6 hours</option>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
            <option value="custom">Custom</option>
        </select>
        <div class="period-bar__custom" hidden>
            <input id="periodFrom" type="text" class="flatpickr" />
            <span>to</span>
            <input id="periodTo" type="text" class="flatpickr" />
        </div>
        <span class="period-bar__summary">from 12:00 to now</span>
        <button class="btn-hsm btn-hsm--primary" id="applyPeriod">Apply</button>
    </div>
}
```

Tree sidebar (`Views/Tree/_Layout.cshtml` + `_Tree.cshtml`) — структура остаётся, но toolbar и нода переоформляются (см. секцию 8).

---

## 8. CSS — скелет

Создать `wwwroot/src/css/home-page.css`, импортировать в `wwwroot/src/index.js`:

```css
/* Layout split */
.sensor-layout {
    display: flex;
    height: calc(100vh - 56px); /* минус top nav */
    background: var(--hsm-bg-page);
}
.tree-sidebar {
    width: 320px;
    flex-shrink: 0;
    background: var(--hsm-surface);
    border-right: 0.5px solid var(--hsm-border-tertiary);
    display: flex;
    flex-direction: column;
    overflow: hidden;
}
.sensor-panel {
    flex: 1;
    display: flex;
    flex-direction: column;
    background: var(--hsm-surface);
    overflow: hidden;
}

/* Tree toolbar */
.tree-toolbar {
    height: 40px;
    padding: 0 12px;
    border-bottom: 0.5px solid var(--hsm-border-tertiary);
    display: flex;
    align-items: center;
    gap: 8px;
    background: var(--hsm-surface);
}
.tree-toolbar__search { position: relative; flex: 1; }
.tree-toolbar__search i {
    position: absolute; left: 9px; top: 8px;
    font-size: 12px; color: var(--hsm-text-tertiary);
}
.tree-toolbar__search input {
    width: 100%; height: 28px;
    border: 0.5px solid var(--hsm-border-secondary);
    border-radius: 6px;
    padding: 0 10px 0 26px;
    font-size: 13px;
    background: var(--hsm-surface);
    outline: none;
}
.tree-toolbar__btn {
    width: 28px; height: 28px;
    border: none; background: transparent;
    color: var(--hsm-text-secondary);
    border-radius: 6px;
    cursor: pointer;
    position: relative;
}
.tree-toolbar__btn:hover { background: var(--hsm-surface-muted); }
.tree-toolbar__btn[data-has-active]::after {
    content: ''; position: absolute; top: 4px; right: 4px;
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--hsm-accent);
}

/* jsTree overrides */
.tree-container { flex: 1; overflow-y: auto; padding: 4px 0; }
.tree-container .jstree-node { font-size: 13px; }
.tree-container .jstree-anchor {
    min-height: 28px; padding: 2px 8px 2px 4px;
    border-radius: 4px;
    color: var(--hsm-text-primary);
}
.tree-container .jstree-anchor:hover { background: rgba(0,0,0,0.03); }
.tree-container .jstree-clicked {
    background: var(--hsm-surface-muted) !important;
    font-weight: 500;
}

/* Status icons */
.tree-icon-ok      { color: var(--hsm-status-ok); }
.tree-icon-error   { color: var(--hsm-status-error); }
.tree-icon-offTime { color: var(--hsm-status-offtime); }
.tree-icon-warning { color: var(--hsm-status-warn); }
.tree-unconfigured-alerts-icon { color: var(--hsm-status-unconf); }

/* Sensor header */
.sensor-header {
    padding: 16px 24px;
    border-bottom: 0.5px solid var(--hsm-border-tertiary);
    background: var(--hsm-surface);
    position: sticky; top: 0; z-index: 2;
}
.sensor-header__crumbs {
    font-size: 12px; color: var(--hsm-text-secondary);
    display: flex; align-items: center; gap: 6px; flex-wrap: wrap;
}
.sensor-header__crumbs a {
    color: var(--hsm-text-secondary);
    text-decoration: none;
}
.sensor-header__crumbs a:hover { color: var(--hsm-accent-dark); }
.sensor-header__crumbs .current {
    color: var(--hsm-text-primary); font-weight: 500;
}
.sensor-header__crumbs .sep { color: var(--hsm-text-tertiary); }

.sensor-header__main {
    display: flex; align-items: center; justify-content: space-between;
    gap: 16px; margin-top: 6px;
}
.sensor-header__name {
    font-size: 18px; font-weight: 500; margin: 0;
    display: inline-flex; align-items: center; gap: 8px;
}
.sensor-header__name .muted-icon {
    font-size: 14px; color: var(--hsm-text-tertiary);
}
.sensor-header__status {
    display: inline-flex; align-items: center; gap: 8px;
    font-size: 13px;
}
.status-dot {
    width: 8px; height: 8px; border-radius: 50%; display: inline-block;
}
.status-dot.status-ok      { background: var(--hsm-status-ok); }
.status-dot.status-error   { background: var(--hsm-status-error); }
.status-dot.status-offtime { background: var(--hsm-status-offtime); }
.status-dot.status-warning { background: var(--hsm-status-warn); }
.status-label              { font-weight: 500; }
.status-label.status-ok      { color: var(--hsm-status-ok); }
.status-label.status-error   { color: var(--hsm-status-error); }
.status-label.status-offtime { color: var(--hsm-status-offtime); }
.status-label.status-warning { color: var(--hsm-status-warn); }
.sensor-value { font-family: var(--font-mono); font-weight: 500; }

.sensor-header__menu {
    width: 32px; height: 32px;
    border: none; background: transparent;
    border-radius: 6px; color: var(--hsm-text-secondary);
    cursor: pointer;
}
.sensor-header__menu:hover { background: var(--hsm-surface-muted); }

.sensor-header__description {
    margin-top: 8px; font-size: 13px; line-height: 1.5;
    color: var(--hsm-text-secondary); max-width: 720px;
}

.sensor-header__alerts { margin-top: 12px; }
.sensor-header__alerts-title {
    font-size: 13px; font-weight: 500; color: var(--hsm-text-secondary);
    margin-bottom: 4px;
}
.sensor-header__alert-line {
    font-size: 13px; color: var(--hsm-text-primary);
    padding-left: 12px; line-height: 1.5;
}
.sensor-header__alerts-empty {
    font-size: 13px; color: var(--hsm-text-tertiary); margin-top: 12px;
}

/* Tabs row */
.sensor-tabs {
    display: flex; align-items: stretch;
    padding: 0 24px;
    background: var(--hsm-surface);
    border-bottom: 0.5px solid var(--hsm-border-tertiary);
    position: sticky; top: <calc header height>; z-index: 2;
    gap: 4px;
}
.sensor-tab {
    height: 40px;
    padding: 0 14px;
    border: none; background: transparent;
    font-size: 13px; font-weight: 500;
    color: var(--hsm-text-secondary);
    display: inline-flex; align-items: center; gap: 6px;
    cursor: pointer;
    border-bottom: 2px solid transparent;
    margin-bottom: -0.5px;
}
.sensor-tab:hover { background: var(--hsm-surface-muted); }
.sensor-tab.is-active {
    color: var(--hsm-text-primary);
    border-bottom-color: var(--hsm-accent);
}

/* Period bar */
.period-bar {
    margin: 12px 24px 0;
    padding: 8px 12px;
    background: var(--hsm-accent-bg);
    border: 0.5px solid #B5D4F4;
    border-radius: var(--hsm-radius-md);
    display: flex; align-items: center; gap: 12px;
    position: sticky; top: <calc header+tabs height>; z-index: 1;
}
.period-bar__quick {
    height: 28px;
    border: 0.5px solid #B5D4F4;
    border-radius: 6px;
    padding: 0 8px;
    font-size: 13px;
    background: var(--hsm-surface);
}
.period-bar__custom { display: flex; align-items: center; gap: 6px; font-size: 13px; }
.period-bar__custom input {
    height: 28px;
    border: 0.5px solid #B5D4F4;
    border-radius: 6px;
    padding: 0 8px;
    font-size: 13px;
    background: var(--hsm-surface);
    width: 140px;
}
.period-bar__summary {
    margin-left: auto;
    font-size: 12px;
    color: var(--hsm-text-secondary);
}

/* Sensor content area */
.sensor-panel__content {
    flex: 1; overflow-y: auto;
    padding: 16px 24px;
}

/* Empty state */
.sensor-empty {
    flex: 1;
    display: flex; flex-direction: column;
    align-items: center; justify-content: center;
    gap: 8px;
    padding: 60px 20px; text-align: center;
}
.sensor-empty i { font-size: 40px; color: var(--hsm-text-tertiary); }
.sensor-empty h3 { font-size: 16px; font-weight: 500; margin: 0; }
.sensor-empty p {
    font-size: 13px; color: var(--hsm-text-secondary); margin: 0;
    max-width: 360px;
}
```

---

## 9. JavaScript — что меняется

Существующая логика polling, jsTree, AJAX-обновления в `Index.cshtml` **остаётся как есть**. Что добавляется:

1. **Tab switching** — простой `addEventListener('click', …)` на `.sensor-tab`. При клике переключает активный, делает AJAX к соответствующему контроллеру для контента таба, заменяет `#sensorTabContent`. Сохранять активный таб в `localStorage` под ключом `hsm:lastTab:<sensorId>` — при возврате к этому сенсору открывать его. **Важно**: текущая Cowork-документация артефактов запрещает localStorage внутри артефактов, но на production-странице — допустимо.
2. **Period bar** — изменения quick-select / datepicker не отправляют запрос немедленно. Кнопка Apply активируется и подсвечивается primary. По клику — AJAX к `SensorHistoryController.ChartHistory` / `TableHistory` / `GetPage` с новыми границами.
3. **Tree filter dropdown** — существующий `_TreeFilter.cshtml` оборачивается в Bootstrap dropdown, открываемый по `fa-sliders` в toolbar'е сайдбара.
4. **Active filter indicator** — после Apply сравнить state с дефолтным; если отличается, навесить `data-has-active` на кнопку `fa-sliders` (см. CSS).
5. **Markdown render** для description — уже работает через `replaceHtmlToMarkdown`. Оставляем.

---

## 10. Accessibility

- Tabs: `role="tablist"`, каждая `role="tab"`, `aria-selected`, контент — `role="tabpanel"`, `aria-labelledby`.
- Breadcrumb: `<nav aria-label="Breadcrumb">`, последний элемент `aria-current="page"`.
- Status — текст рядом со статус-дотом нужен для скринридеров, а не только цвет.
- Кнопка `fa-ellipsis-vertical` обязательно `aria-label="Sensor actions"`.
- Tree (jsTree) уже a11y-аккуратный; проверить, что после CSS-override не сломался keyboard focus.
- Период: лейбл "Time period" перед селектором (visually-hidden), кнопка Apply должна получать фокус последней.

---

## 11. Адаптив

| Ширина          | Поведение                                                                              |
|----------------|----------------------------------------------------------------------------------------|
| ≥ 1200 px      | Базовая раскладка. Сайдбар 320 px.                                                      |
| 992–1199 px    | Сайдбар 280 px.                                                                         |
| 768–991 px     | Сайдбар сворачивается в off-canvas (Bootstrap `offcanvas-start`). В top nav появляется кнопка `fa-bars` для открытия. |
| < 768 px       | Off-canvas; tabs row горизонтально скроллится; period bar — каждый элемент на новой строке.  |

---

## 12. QA чеклист

- [ ] При выборе сенсора в дереве правая панель обновляется без перезагрузки.
- [ ] Breadcrumb показывает корректный путь и позволяет вернуться к родительскому узлу.
- [ ] Имя сенсора и статус-плашка обновляются по polling'у каждые `TreeUpdateInterval` секунд.
- [ ] Description рендерится как markdown.
- [ ] Список алертов в header'е — plain text, без UI-элементов.
- [ ] Табы рендерятся по типу узла (Graph только для numeric, Files только для file sensor, и т.д.).
- [ ] Period bar виден только на Graph / Table / Journal. Apply отправляет правильный диапазон.
- [ ] Quick-select переключает диапазон без открытия datepicker.
- [ ] При переключении узла активный таб сохраняется (если применим к новому узлу) или сбрасывается на дефолтный.
- [ ] Stale-индикатор работает, validation-error баннер появляется при Status === Error.
- [ ] Tree filter dropdown открывается, Apply сохраняет настройки. Иконка фильтра получает blue dot при не-дефолтных настройках.
- [ ] Search в сайдбаре фильтрует видимые узлы.
- [ ] Alerts tab позволяет создать / редактировать / удалить правило.
- [ ] Settings tab показывает sticky-bar Cancel / Save только при изменениях.
- [ ] Empty state на пустой панели и пустых алертах.
- [ ] Off-canvas сайдбар на узких экранах работает.
- [ ] Скрин-ридер: можно дойти до сенсора в дереве, услышать его статус, переключиться на таб клавиатурой.

---

## 13. Что выкидываем

- Inline-стили `style="min-width: 600px;"` в `Index.cshtml` — переносим в CSS.
- `.hidden_element` класс на `#nodeDataPanel` — заменить на `[hidden]` или `display: none` в empty state, чтобы не плодить пользовательские классы видимости.
- Spinner `nodeDataSpinner` оставляем, но переделываем визуально: убираем класс `spinner-border` (Bootstrap) на 24×24 svg-кружок в `--hsm-accent`.
- `accordion`-обвязка вокруг общих секций header'а — убираем, секции в header'е открыты по умолчанию и не сворачиваются.
- Старый `_GeneralInfo.cshtml` редизайнить как `Settings tab content` (см. 4.5 Settings).
- Кнопки Edit / Cancel / Save в header'е переносим внутрь Settings tab. В header'е остаётся только меню `fa-ellipsis-vertical`.
- Таблицы с `table-striped` — убираем zebra, оставляем border-top.

---

## 14. Шаги внедрения

1. Создать `wwwroot/src/css/home-page.css` с разметкой из секции 8. Импортировать в `wwwroot/src/index.js`.
2. Переделать `_NodeDataPanel.cshtml` по структуре из секции 7.
3. Создать новые partials: `_SensorHeader.cshtml`, `_SensorHeaderAlerts.cshtml`, `_TabsRow.cshtml`, `_PeriodBar.cshtml`, `_EmptyState.cshtml`.
4. Перенести существующие табы (`_GeneralInfo`, `_SensorGeneralInfo`, history-таблицы, plot) под новые названия и обёртки.
5. Обновить `_TreeFilter.cshtml` чтобы он открывался как dropdown от иконки `fa-sliders` в новом toolbar'е сайдбара.
6. Обновить `_Layout.cshtml` (Tree) — обернуть всё в `.sensor-layout`, привести toolbar к новому виду.
7. Tab-switching JS подключить в существующий entry (`wwwroot/src/index.js` или новый `home.ts`).
8. Прогнать чеклист из секции 12 в dev-сборке: `npm run build_dev` из `src/server/HSMServer/`.
9. Скриншоты до/после, приложить к PR.

---

## 15. Связанные документы

- `docs/auth-redesign.md` — Login / Registration: общие принципы, бренд-блок, баннеры, кнопки.
- `docs/products-redesign.md` — Products: общие принципы для таблиц, поиска, фолдеров и empty states.
- `wwwroot/src/css/theme.css` — все CSS-токены, используемые в этом документе.
