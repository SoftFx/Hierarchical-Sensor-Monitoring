# Products page — design requirements

Дизайн-спецификация и руководство для разработчика по странице `Views/Product/Index.cshtml`. Документ соответствует общему стилю проекта (см. `docs/auth-redesign.md`) — модерн-минимализм, Bootstrap 5.3.2 как база, кастомизация через CSS-переменные.

---

## 1. Цели и принципы

1. Сделать страницу спокойнее визуально: больше воздуха, тоньше границы, меньше акцентов.
2. Дать **одну точку входа в поиск** — пользователь не должен думать, в каком фолдере искать.
3. Сохранить иерархию "Folder → Product" как основу, но не делать её доминирующей формой.
4. Не терять текущий контекстный смысл: продукты без папки должны быть видны, но не доминировать.
5. Не вводить новые цвета и формы — переиспользуем токены из `theme.css`.

Принципы во всём проекте:

- Только два веса шрифта: 400 (regular), 500 (medium). Никакого 600/700.
- Sentence case везде. Никаких ALL CAPS и Title Case.
- Границы — `0.5px solid var(--hsm-border-tertiary)` (или `--hsm-border-secondary` для эмфазы).
- Радиусы — `var(--hsm-radius-md)` (8px) для кнопок/инпутов, `var(--hsm-radius-lg)` (12px) для карточек.
- Никаких теней, кроме фокус-кольца на инпутах.
- Иконки — FontAwesome 6.2.1, уже подключённый в проекте.

---

## 2. Анатомия страницы

Сверху вниз:

1. **Top nav** (общий из `_Layout.cshtml`, не меняется в этом документе).
2. **Page header** — заголовок "Products", саб-каунтер ("3 folders · 14 products · 2 without folder"), правая группа кнопок (Add folder / Add product).
3. **Search toolbar** — глобальный поиск по продуктам + фильтр по менеджеру, белая карточка во всю ширину контейнера.
4. **Список фолдеров** — каждый как карточка-аккордеон. Развёрнутый фолдер показывает таблицу продуктов.
5. **Without folder** — псевдо-блок в самом низу, отличается визуально (пунктирная рамка, приглушённые цвета). Рендерится только если есть продукты без папки.

Общий вертикальный ритм: 14–16 px между блоками верхней зоны, 12 px между фолдерами, 20 px зазор перед блоком "Without folder".

---

## 3. Дизайн-токены

Все значения уже определены в `wwwroot/src/css/theme.css`. На этой странице используются:

```
Цвета:
  --hsm-bg-page         — фон страницы (нейтральный светлый)
  --hsm-surface          — фон карточек, белый
  --hsm-surface-muted    — фон thead и шапки folder, очень светлый (#FAFAF7)
  --hsm-border-tertiary  — 0.5px бордеры по умолчанию
  --hsm-border-secondary — 0.5px бордеры на инпутах и outline-кнопках
  --hsm-text-primary     — основной текст
  --hsm-text-secondary   — вторичный текст (саб-каунтеры, метки)
  --hsm-text-tertiary    — приглушённый (плейсхолдеры, иконки в инпутах, "---")

Статусные:
  --hsm-accent           — #378ADD (primary action, активный фолдер)
  --hsm-accent-bg        — #E6F1FB (фон активной таб-пилюли)
  --hsm-warn             — #BA7517 (стейл апдейт, фолдер требует внимания)

Радиусы и отступы:
  --hsm-radius-md  = 8px
  --hsm-radius-lg  = 12px
  --hsm-space-2 = 8px
  --hsm-space-3 = 12px
  --hsm-space-4 = 16px
  --hsm-space-5 = 20px

Типографика:
  Heading h2  = 18px / 500
  Section title = 14px / 500
  Body        = 13px / 400
  Caption / counter = 12px / 400, --hsm-text-secondary
  Label / hint = 11–12px / 400, --hsm-text-tertiary
```

---

## 4. Компоненты в деталях

### 4.1. Page header

```
+-----------------------------------------------------+
| Products                  [+ Add folder] [+ Add ...]|
| 3 folders · 14 products · 2 without folder          |
+-----------------------------------------------------+
```

- Заголовок: `h2`, 18px / 500, без отступа сверху.
- Саб-каунтер: 12px, `--hsm-text-secondary`, отступ сверху 2 px. Формат: `<X> folders · <Y> products[· <Z> without folder]`. Последний сегмент скрыть, если `Z === 0`.
- Правая группа: две кнопки, gap 8 px:
  - **Add folder** — outline-кнопка (`background: var(--hsm-surface)`, border `0.5px solid var(--hsm-border-secondary)`, текст `--hsm-text-primary`).
  - **Add product** — primary-кнопка (`background: var(--hsm-accent)`, без бордера, текст `#fff`, `font-weight: 500`).
- Высота кнопок 32 px, padding 0 12 px, иконка слева (`fa-folder-plus` / `fa-plus`), gap 6 px между иконкой и текстом.
- Видимость кнопок управляется правами (`UserRoleHelper.IsUserCRUDAllowed`).

### 4.2. Global search toolbar

```
+-----------------------------------------------------+
| 🔍 Search products across all folders   👤 Manager  |
+-----------------------------------------------------+
```

- Белая карточка во всю ширину: `background: var(--hsm-surface); border: 0.5px solid var(--hsm-border-tertiary); border-radius: var(--hsm-radius-lg); padding: 10px 12px;`.
- Внутри — два инпута через `display: flex; gap: 8px`. Соотношение `flex: 2` (Search) и `flex: 1` (Manager).
- Инпуты: высота 32 px, `border: 0.5px solid var(--hsm-border-secondary)`, радиус 6 px, font-size 13 px, padding `0 10px 0 30px` (под иконку). Иконка `fa-magnifying-glass` / `fa-user` внутри слева, абсолютно позиционирована.
- При не пустом значении в правой стороне инпута показать `fa-xmark` (clear button).
- Опционально — справа от инпутов разделитель `0.5px × 20px` + текст "<N> matches in <M> folders" при активном поиске.
- На фокус — `border-color: var(--hsm-accent)` и `box-shadow: 0 0 0 3px rgba(55,138,221,0.15)`.

#### Поведение поиска

| Состояние                  | Что происходит                                                                 |
|---------------------------|--------------------------------------------------------------------------------|
| Запрос пуст               | Все фолдеры в исходном состоянии (по дефолту все развёрнуты, как сейчас).        |
| Запрос есть, совпадения   | Фолдеры с совпадениями авто-разворачиваются. Несовпадающие строки скрыты.        |
| Запрос есть, совпадение в имени | В колонке Name совпадающий фрагмент подсвечен `<mark>` (фон `#FAEEDA`, текст без изменений). |
| Фолдер без совпадений     | Шапка фолдера остаётся видна, opacity `0.55`, под счётчиком — "no matches".     |
| Совсем нет совпадений     | Под тулбаром — empty state: иконка `fa-magnifying-glass`, "Nothing matches «query»". |

Поиск работает по полю Name (фронтенд-фильтр или серверный — на усмотрение разработчика; текущий код использует серверный POST в `FilterFolderProducts`).

#### Без папок и поиск

Блок "Without folder" участвует в фильтрации **на равных** с фолдерами. Если совпадения только там — фолдеры выше показываются свёрнутыми + "no matches".

### 4.3. Folder accordion

Каждый фолдер — карточка `background: var(--hsm-surface); border: 0.5px solid var(--hsm-border-tertiary); border-radius: var(--hsm-radius-lg);`.

#### Шапка фолдера (всегда видна)

```
+-----------------------------------------------------+
|  ›  ■  Production            8 products       ⋮     |
+-----------------------------------------------------+
```

- Padding `12px 16px`. Display flex, gap 10 px, align-items center.
- Слева — иконка-стрелка: `fa-chevron-right` (свернут) / `fa-chevron-down` (развёрнут). Font-size 14 px, цвет `--hsm-text-secondary`.
- Затем — статус-квадратик `8×8 px`, `border-radius: 2px`, цвет берётся из `Folder.BackgroundColor` (текущий проект). Если цвет фолдера не задан — `--hsm-text-tertiary`.
- Затем — название фолдера: 14 px / 500.
- Затем — счётчик: 12 px, `--hsm-text-secondary`. Формат "<N> products" в обычном состоянии, "<X> of <Y> shown" при активном поиске.
- Справа (`margin-left: auto`) — `fa-ellipsis-vertical` для дропдауна действий с фолдером (Edit, Remove, и т.д.).

При развёрнутом состоянии шапка получает `border-bottom: 0.5px solid var(--hsm-border-tertiary)`.

Поле описания фолдера (`Model.Description`) сейчас лежит в шапке — **переносим**: показываем под названием как 13 px / 400, `--hsm-text-secondary`, в отдельной строке (`display: block; margin-top: 4px`), либо как тултип на наведении на название (если описание длиннее одной строки).

#### Тело фолдера (только при развёрнутом)

Таблица продуктов (см. 4.4) занимает всю ширину карточки без внутренних отступов — границы таблицы примыкают к границам карточки.

Под таблицей опционально — строка "Telegram chats: chatA, chatB" (как сейчас в `_FolderAccordion.cshtml`). Стиль: padding 10px 16px, 13 px, `--hsm-text-secondary`. Чаты — ссылки `--hsm-accent`. Если чатов нет — "No telegram chats" тем же стилем без жирного.

### 4.4. Products table

Внутри развёрнутого фолдера.

```
+-----------------------------------------------------+
| Name             | Managers          | Last update ⇅|  ←  thead, фон --hsm-surface-muted
+------------------+-------------------+--------------+
| Sensor Lab        | m.pazniak, ...   | 2 min ago    | ⋮
| Edge Gateway      | a.kolesnik       | 2 days ago ⚠ | ⋮
| Climate Monitor   | ---              | 14 min ago   | ⋮
+-----------------------------------------------------+
```

Колонки и ширины:

| Колонка       | Ширина | Контент                                                                                   |
|---------------|--------|-------------------------------------------------------------------------------------------|
| Name          | 40%    | Ссылка на edit (если есть права). Цвет `--hsm-accent-dark` (#185FA5), `text-decoration: none`, `font-weight: 500`. Иначе — текст. |
| Managers      | 30%    | Список через запятую, 13 px, `--hsm-text-secondary`. Если пусто — `---` цветом `--hsm-text-tertiary`. |
| Last update   | 22%    | Relative-time (`ShortLastUpdateTime`), 13 px, `--hsm-text-secondary`. Если просрочено — справа от текста иконка `fa-triangle-exclamation` цветом `--hsm-warn`, с тултипом, как сейчас. |
| Actions       | 8%     | `fa-ellipsis-vertical` (если есть права) или disabled-иконка. Центрировано.                |

thead: padding 8 16, font 12 px / 500, `--hsm-text-secondary`. На сортируемых колонках — иконка `fa-sort` (или `fa-sort-up`/`fa-sort-down` при активной сортировке), inline-flex с gap 4 px.

tbody:
- Каждая строка — `border-top: 0.5px solid var(--hsm-border-tertiary)`. Первая строка тоже отделена от thead этим бордером.
- Padding `10px 16px`.
- **Никаких zebra-stripes** (`table-striped` убираем) — оставляем только разделители между строк. Это снижает шумность.
- Hover на строке — `background: var(--hsm-surface-muted)`.

Dropdown actions (Edit / Remove / Move to ...) — Bootstrap dropdown, текст 13 px, `font-weight: 400`. Опасное действие "Remove" — цвет `--hsm-danger` (#A32D2D).

### 4.5. Without folder (псевдо-блок)

Структура та же, что у обычного фолдера, но визуально приглушённая:

- Карточка: `border: 0.5px **dashed** var(--hsm-border-secondary)` вместо solid.
- Шапка: `background: var(--hsm-surface-muted)`, разделитель шапки тоже dashed.
- Иконка вместо статус-квадратика — `fa-folder-xmark` (или `fa-folder-open` приглушённая), цвет `--hsm-text-tertiary`.
- Название "Without folder" цветом `--hsm-text-secondary` (не primary).
- Справа в шапке — подсказка `fa-circle-info` + текст "Assign products to a folder for grouping", 11 px, `--hsm-text-tertiary`.
- Таблица внутри — та же, что у обычных фолдеров.
- Позиция — **последним блоком списка**, с зазором сверху 20 px от последнего обычного фолдера.
- Рендерится только если есть хотя бы один продукт без папки.

В меню каждой строки этого блока обязательно есть пункт "Move to folder" → подменю со списком существующих папок.

### 4.6. Пустые состояния

| Сценарий                                  | Что показываем                                                                       |
|-------------------------------------------|--------------------------------------------------------------------------------------|
| Нет ни одной папки и продуктов            | Под тулбаром — иконка `fa-folder-tree` (40 px, `--hsm-text-tertiary`), заголовок "No products yet", подпись "Create a folder and add your first product", primary-кнопка "Add product". |
| Папка пустая и развёрнута                 | В теле фолдера — строка 13 px, `--hsm-text-tertiary`, "This folder is empty". Без кнопок. |
| Поиск ничего не нашёл                     | Под тулбаром — иконка `fa-magnifying-glass`, текст "Nothing matches «<query>»", ссылка "Clear search". Сами фолдеры скрыть. |

---

## 5. Состояния и интеракции

| Элемент              | Состояние   | Поведение                                                                                |
|---------------------|------------|------------------------------------------------------------------------------------------|
| Add folder/product  | hover      | Outline → `background: var(--hsm-surface-muted)`. Primary → `background: #1F6FC4` (на тон темнее). |
| Add folder/product  | active     | `transform: scale(0.98)`.                                                                |
| Search input        | focus      | Border `--hsm-accent`, кольцо `0 0 0 3px rgba(55,138,221,0.15)`.                          |
| Folder header       | hover      | Курсор pointer на всю шапку, лёгкая подсветка фона `--hsm-surface-muted`.                |
| Folder header       | expanded   | Угол `fa-chevron-down`, нижний бордер шапки виден.                                       |
| Product row         | hover      | `background: var(--hsm-surface-muted)`.                                                  |
| Sort header         | active     | Иконка `fa-sort-up` или `fa-sort-down`, цвет `--hsm-text-primary` (не secondary).        |
| Stale product       | —          | Иконка `fa-triangle-exclamation` справа от времени, цвет `--hsm-warn`, с tooltip.        |
| Search match        | —          | `<mark>` обёртка, `background: #FAEEDA`, без рамки.                                       |

Анимации — только `transition: background 120ms ease, border-color 120ms ease` на интерактивных элементах. Никаких `transform`-анимаций кроме `scale(0.98)` на active.

---

## 6. FontAwesome icon mapping

Проект использует FontAwesome 6.2.1. В мокапах рендерятся Tabler-иконки — вот соответствие:

| В мокапе (Tabler) | В коде (FontAwesome)        | Где                                |
|-------------------|----------------------------|------------------------------------|
| ti-plus           | fa-plus                    | Add product, Add folder            |
| ti-folder-plus    | fa-folder-plus             | Add folder                         |
| ti-search         | fa-magnifying-glass        | Search input                       |
| ti-user           | fa-user                    | Manager filter input               |
| ti-x              | fa-xmark                   | Clear search button                |
| ti-chevron-right  | fa-chevron-right           | Свёрнутый фолдер                   |
| ti-chevron-down   | fa-chevron-down            | Развёрнутый фолдер                 |
| ti-arrows-sort    | fa-sort                    | Сортируемая колонка                |
| ti-dots-vertical  | fa-ellipsis-vertical       | Actions меню (фолдер и продукт)    |
| ti-alert-triangle | fa-triangle-exclamation    | Stale product                      |
| ti-folder-off     | fa-folder-xmark            | Without folder pseudo-block        |
| ti-info-circle    | fa-circle-info             | Hint в шапке "Without folder"      |
| ti-folder-tree    | fa-folder-tree             | Empty state                        |

Размер иконок — наследует font-size родителя; для inline-иконок в строке 14 px, в полях ввода 13 px, в шапке "Without folder" 14 px.

---

## 7. Razor markup — рекомендации

`Views/Product/Index.cshtml` пересобирается следующим образом:

```cshtml
@model List<FolderViewModel>

@{
    ViewData["Title"] = "Products";
    var folders         = Model.Where(f => f.Id.HasValue).ToList();
    var unassigned      = Model.FirstOrDefault(f => !f.Id.HasValue);
    var totalProducts   = Model.Sum(f => f.Products.Count);
    var unassignedCount = unassigned?.Products.Count ?? 0;
}

<div class="products-page">
    <header class="products-header">
        <div>
            <h2>Products</h2>
            <div class="products-subcount">
                @folders.Count folders · @totalProducts products
                @if (unassignedCount > 0) { <span>· @unassignedCount without folder</span> }
            </div>
        </div>
        <div class="products-actions">
            @if (UserRoleHelper.IsUserCRUDAllowed(Context.User as User))
            {
                <a class="btn-hsm btn-hsm--outline" asp-controller="Folders" asp-action="EditFolder">
                    <i class="fa-solid fa-folder-plus"></i> Add folder
                </a>
                <a class="btn-hsm btn-hsm--primary" href="javascript:showAddProductModal();">
                    <i class="fa-solid fa-plus"></i> Add product
                </a>
            }
        </div>
    </header>

    <div class="products-search">
        <div class="products-search__field">
            <i class="fa-solid fa-magnifying-glass"></i>
            <input id="globalProductSearch" placeholder="Search products across all folders" />
        </div>
        <div class="products-search__field products-search__field--narrow">
            <i class="fa-solid fa-user"></i>
            <input id="globalManagerSearch" placeholder="Manager" />
        </div>
    </div>

    <div id="folderList">
        @foreach (var folder in folders)
        {
            @await Html.PartialAsync("_FolderAccordion", folder)
        }

        @if (unassigned is not null && unassignedCount > 0)
        {
            @await Html.PartialAsync("_UnassignedProductsBlock", unassigned)
        }
    </div>
</div>

@await Html.PartialAsync("_AddProductModal", new AddProductViewModel())
@await Html.PartialAsync("~/Views/Shared/_ConfirmationModal.cshtml")
```

`_FolderAccordion.cshtml` упрощается: текущий внутренний фильтр (поля Name / Manager в `_ProductList.cshtml`) убираем, оставляем только таблицу. Логику фильтрации поднимаем в общий `globalProductSearch` (см. секцию 9).

`_UnassignedProductsBlock.cshtml` — новый partial: та же таблица, но с пунктирными границами и кастомной шапкой. Структурно — упрощённая копия `_FolderAccordion.cshtml`.

---

## 8. CSS — что нужно добавить

В `wwwroot/src/css/product.css` (либо в новый `wwwroot/src/css/products-page.css` и подключить из `index.js`):

```css
.products-page {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}

.products-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 14px;
}
.products-header h2 {
    font-size: 18px;
    font-weight: 500;
    margin: 0;
}
.products-subcount {
    font-size: 12px;
    color: var(--hsm-text-secondary);
    margin-top: 2px;
}

.products-actions {
    display: flex;
    gap: 8px;
}

.btn-hsm {
    height: 32px;
    padding: 0 12px;
    border-radius: var(--hsm-radius-md);
    font-size: 13px;
    display: inline-flex;
    align-items: center;
    gap: 6px;
    text-decoration: none;
    transition: background 120ms ease, border-color 120ms ease;
}
.btn-hsm--outline {
    background: var(--hsm-surface);
    border: 0.5px solid var(--hsm-border-secondary);
    color: var(--hsm-text-primary);
}
.btn-hsm--outline:hover { background: var(--hsm-surface-muted); }
.btn-hsm--primary {
    background: var(--hsm-accent);
    border: none;
    color: #fff;
    font-weight: 500;
}
.btn-hsm--primary:hover { background: #1F6FC4; }
.btn-hsm:active { transform: scale(0.98); }

.products-search {
    background: var(--hsm-surface);
    border: 0.5px solid var(--hsm-border-tertiary);
    border-radius: var(--hsm-radius-lg);
    padding: 10px 12px;
    display: flex;
    gap: 8px;
    margin-bottom: 14px;
}
.products-search__field {
    position: relative;
    flex: 2;
}
.products-search__field--narrow { flex: 1; }
.products-search__field i {
    position: absolute;
    left: 10px;
    top: 9px;
    font-size: 13px;
    color: var(--hsm-text-tertiary);
}
.products-search__field input {
    width: 100%;
    height: 32px;
    border: 0.5px solid var(--hsm-border-secondary);
    border-radius: 6px;
    padding: 0 28px 0 30px;
    font-size: 13px;
    background: var(--hsm-surface);
    outline: none;
}
.products-search__field input:focus {
    border-color: var(--hsm-accent);
    box-shadow: 0 0 0 3px rgba(55,138,221,0.15);
}

/* Folder accordion */
.folder-card {
    background: var(--hsm-surface);
    border: 0.5px solid var(--hsm-border-tertiary);
    border-radius: var(--hsm-radius-lg);
    margin-bottom: 12px;
    overflow: hidden;
}
.folder-card__header {
    padding: 12px 16px;
    display: flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
    transition: background 120ms ease;
}
.folder-card__header:hover { background: var(--hsm-surface-muted); }
.folder-card--open .folder-card__header { border-bottom: 0.5px solid var(--hsm-border-tertiary); }
.folder-card__status {
    width: 8px;
    height: 8px;
    border-radius: 2px;
    background: var(--hsm-text-tertiary);
}
.folder-card__name { font-size: 14px; font-weight: 500; }
.folder-card__count { font-size: 12px; color: var(--hsm-text-secondary); }
.folder-card__menu { margin-left: auto; color: var(--hsm-text-tertiary); }

/* Products table */
.products-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 13px;
}
.products-table thead {
    background: var(--hsm-surface-muted);
}
.products-table th {
    text-align: left;
    padding: 8px 16px;
    font-weight: 500;
    font-size: 12px;
    color: var(--hsm-text-secondary);
}
.products-table tbody tr { border-top: 0.5px solid var(--hsm-border-tertiary); transition: background 120ms ease; }
.products-table tbody tr:hover { background: var(--hsm-surface-muted); }
.products-table td { padding: 10px 16px; }
.products-table .col-name { width: 40%; }
.products-table .col-managers { width: 30%; }
.products-table .col-update { width: 22%; }
.products-table .col-actions { width: 8%; text-align: center; }
.products-table .col-name a {
    color: #185FA5;
    text-decoration: none;
    font-weight: 500;
}
.products-table .col-managers .muted { color: var(--hsm-text-tertiary); }
.products-table .stale-icon { color: var(--hsm-warn); margin-left: 6px; }

/* Without folder */
.unassigned-card {
    background: var(--hsm-surface);
    border: 0.5px dashed var(--hsm-border-secondary);
    border-radius: var(--hsm-radius-lg);
    margin-top: 20px;
    overflow: hidden;
}
.unassigned-card .folder-card__header {
    background: var(--hsm-surface-muted);
    border-bottom: 0.5px dashed var(--hsm-border-tertiary);
}
.unassigned-card .folder-card__name {
    color: var(--hsm-text-secondary);
}
.unassigned-card .folder-card__hint {
    font-size: 11px;
    color: var(--hsm-text-tertiary);
    margin-left: auto;
    display: inline-flex;
    align-items: center;
    gap: 4px;
}

/* Search match */
.products-table mark {
    background: #FAEEDA;
    color: inherit;
    padding: 1px 2px;
    border-radius: 2px;
}

/* Empty / no-match states */
.products-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding: 40px 20px;
    color: var(--hsm-text-secondary);
    font-size: 13px;
}
.products-empty i { font-size: 32px; color: var(--hsm-text-tertiary); }
```

В `wwwroot/src/index.js` импортировать новый CSS, если выделили в отдельный файл.

---

## 9. JavaScript — глобальный поиск

Минимальный фронтенд-фильтр (без сервера) на ванильном JS:

```js
const search = document.getElementById('globalProductSearch');
const managerSearch = document.getElementById('globalManagerSearch');

function applyProductFilter() {
    const q  = search.value.trim().toLowerCase();
    const mq = managerSearch.value.trim().toLowerCase();

    document.querySelectorAll('.folder-card, .unassigned-card').forEach((card) => {
        let visibleRows = 0;
        let totalRows   = 0;

        card.querySelectorAll('.products-table tbody tr').forEach((tr) => {
            totalRows++;
            const name = tr.querySelector('.col-name')?.textContent.toLowerCase() || '';
            const mgr  = tr.querySelector('.col-managers')?.textContent.toLowerCase() || '';

            const matches = (q === '' || name.includes(q)) && (mq === '' || mgr.includes(mq));
            tr.hidden = !matches;
            if (matches) visibleRows++;
        });

        // Update counter
        const counter = card.querySelector('.folder-card__count');
        if (counter) {
            counter.textContent = (q || mq)
                ? `${visibleRows} of ${totalRows} shown`
                : `${totalRows} products`;
        }

        // Dim / collapse non-matching folders
        if ((q || mq) && visibleRows === 0) {
            card.style.opacity = '0.55';
            if (counter) counter.textContent = 'no matches';
        } else {
            card.style.opacity = '';
        }
    });
}

search.addEventListener('input', applyProductFilter);
managerSearch.addEventListener('input', applyProductFilter);
```

Для серверного режима — POST на `FilterFolderProducts` с дополнительным параметром `globalQuery`, контроллер фильтрует все фолдеры и возвращает обновлённый `_ProductList` для каждого.

Highlight совпадений — после фильтрации обернуть найденные подстроки в `<mark>`:

```js
function highlight(el, query) {
    if (!query) return;
    const text = el.textContent;
    const idx = text.toLowerCase().indexOf(query.toLowerCase());
    if (idx === -1) return;
    el.innerHTML =
        text.slice(0, idx) +
        '<mark>' + text.slice(idx, idx + query.length) + '</mark>' +
        text.slice(idx + query.length);
}
```

---

## 10. Accessibility

- Заголовок страницы — `h2`, единственный на странице, после `h1` из layout (если есть).
- Каждый фолдер — `<section aria-labelledby="...">`. Шапка фолдера — `<button>` с `aria-expanded` и `aria-controls`.
- Таблица — нормальный `<table>` с `<thead>` и `<tbody>`. На сортируемых колонках — `aria-sort="ascending|descending|none"`.
- Инпуты поиска — `<label class="sr-only">` для скринридеров + `placeholder` для зрячих.
- `fa-...` иконки — `aria-hidden="true"`. Иконка stale-продукта — `aria-label` с описанием.
- Кнопки `fa-ellipsis-vertical` без текста — `aria-label="Actions for <name>"`.
- Фокус на интерактивных элементах виден: `:focus-visible { outline: 2px solid var(--hsm-accent); outline-offset: 2px; }`.
- Минимальный контраст текста — 4.5:1 (проверить `--hsm-text-secondary` и `--hsm-text-tertiary` на фоне `--hsm-surface-muted`).

---

## 11. Адаптив

| Ширина          | Поведение                                                                                |
|-----------------|------------------------------------------------------------------------------------------|
| ≥ 992 px         | Раскладка по макету: header в одну строку, search в одну строку, таблица — 4 колонки.   |
| 768–991 px       | Колонка Managers сжимается; иконка sort в header переносится под текст.                  |
| 576–767 px       | Кнопки header переезжают на отдельную строку под заголовок. Search — оба инпута во всю ширину, столбиком. |
| < 576 px         | Таблица переключается на card-layout: каждый продукт — карточка с тремя строками (Name, Managers, Last update + actions справа). |

В `_Layout.cshtml` контент уже завёрнут в флекс с ограничением — дополнительно ничего не нужно.

---

## 12. QA чеклист

- [ ] Заголовок и счётчик корректны при разных комбинациях (нет папок, нет продуктов, есть unassigned).
- [ ] Add folder и Add product видны только тем, у кого `IsUserCRUDAllowed`.
- [ ] Поиск работает по обеим колонкам (Name, Managers) и обновляет счётчики в каждом фолдере.
- [ ] Очистка поиска возвращает все строки и исходные счётчики.
- [ ] Кнопка `fa-xmark` в инпуте поиска появляется только при не пустом значении.
- [ ] Совпадение подсвечено `<mark>`.
- [ ] Фолдеры без совпадений — opacity 0.55, "no matches".
- [ ] Блок Without folder показывается только если есть продукты без папки, всегда последним.
- [ ] Sort по Last update работает в обе стороны, иконка меняется.
- [ ] Stale-product иконка показывается с tooltip.
- [ ] Move to folder в меню строки работает; в подменю — все папки кроме текущей и кроме самого "Without folder".
- [ ] Hover на строку — подсветка `--hsm-surface-muted`.
- [ ] Empty state показывается, когда нет ни одного продукта.
- [ ] Мобильная раскладка читаема на 360 px.
- [ ] Скрин-ридер (NVDA / VoiceOver): можно дойти до каждой строки, понять, в каком фолдере она лежит.

---

## 13. Что выкидываем из текущей реализации

Чтобы новый макет встал чисто, нужно убрать:

- Локальные поля `searchProduct` / `searchManager` внутри `_ProductList.cshtml` (заменяются глобальным поиском).
- `table-striped` класс на таблице — оставляем чистый `<table>` с разделителями.
- Inline-стили `style="border-color:...; background:...; color:..."` в `_FolderAccordion.cshtml` — переносим в CSS-класс `.folder-card[data-color="..."]` или оставляем только статус-квадратик с цветом из `BackgroundColor`.
- `accordion-button::after` (стандартный Bootstrap-каретка) — заменяем нашей `fa-chevron-down`.
- `m-2`, `my-2`, `p-3`, прочие Bootstrap-утилиты в шаблонах — переносим в CSS-классы спецификации.

---

## 14. Шаги внедрения

1. Создать `_UnassignedProductsBlock.cshtml` (копия `_FolderAccordion` без accordion-обвязки).
2. Обновить `Views/Product/Index.cshtml` по разметке из секции 7.
3. Упростить `_FolderAccordion.cshtml`: убрать accordion-button стандарт Bootstrap, заменить на свой кликабельный `.folder-card__header`. Убрать локальные поля поиска из `_ProductList.cshtml`.
4. Добавить CSS из секции 8 в `wwwroot/src/css/product.css`. Если файл переписывается полностью — удалить старые конфликтующие селекторы.
5. Подключить JS из секции 9 — либо в существующий `wwwroot/src/ts/products.ts`, либо новый файл, импортируемый в `index.js`.
6. Прогнать чеклист из секции 12 в dev-сборке: `npm run build_dev` из `src/server/HSMServer/`.
7. Снять скриншоты до/после, приложить к PR.
