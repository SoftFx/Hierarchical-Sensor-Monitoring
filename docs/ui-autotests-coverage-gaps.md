# UI Autotests — покрытие и пробелы

Аудит Playwright-сюита `src/tests/Autotests/` (23 спеки).
Дата: 2026-07-17. Ветка: `fix/e2e-folder-chats-selectors`, после pull из `master`.

---

## Текущее покрытие (23 спеки)

**Сильные места**
- `alert_schedules/alert_schedules.spec.ts` — эталон по качеству: полный CRUD + валидация + негативные + persist-after-reload.
- RBAC/пользователи: регистрация с валидацией, смена пароля, viewer→admin, видимость admin-вкладок.
- Products/Folders: happy-path CRUD.
- Dashboards: CRUD.

**Слабые паттерны в самом сюите**
- Только Chromium, `workers: 1`.
- Нет `data-testid` — селекторы привязаны к Bootstrap-классам/id/DOM-структуре (ломаются при рефакторинге; уже было с «Telegram»→«Chats»).
- Захардкоженные имена (`TestProduct`, `TestAutoFolder`) без `afterEach` — протекают при падении.
- Тонкие негативные тесты вне `alert_schedules` и `register_new_user`.
- Три конкурирующих системы cleanup (`fixtures.ts` cleanup.*, `cleanup.ts` remove*ByName, локальные per-spec).
- Дубли (см. «Почистить»).

### Покрытие по областям

| Область | Спеки | Замечание |
|---|---|---|
| Auth | login_success, unsuccessful_login | один негатив (неверный пароль) |
| Products | product_add_edit_remove, product_edit_allsettings, check_product_inTheTree, tests_api_create_sensor | нет валидации/дубликата |
| Folders | add_remove_folder, modify_folder_* (general/settings/user/telegramm), add_folders_products | boilerplate создания дублируется 6× |
| Access keys | access_key_settings, product_edit_allsettings | проверено дважды |
| Dashboards | add_remove_modify_dashboard, add_modify_remove_panel | #9 поглощён #10 |
| Alert templates | add_remove, check_fields_templates | нет валидации, нет bind-to-sensor |
| Alert schedules | alert_schedules | глубокое покрытие |
| Users/RBAC | add_users, register_new_user, admin_settings, change_password_for_user, change_viewer_to_admin | только позитив |
| Home | check_product_inTheTree, tests_api_create_sensor | «open details» выпилен (#1199) |

---

## 🟢 Тир 1 — крупные непокрытые области (полный ноль, операторский UI)

| Новая спека | Что покрывает |
|---|---|
| `home/sensor_alert_editor.spec.ts` | Пер-сенсорный редактор алертов: condition + action на сенсоре, гетерогенный выбор назначения (Telegram users/groups + Slack), TTL/inactivity как condition, test-toast preview. Сейчас покрыты только глобальные AlertTemplates. |
| `home/history/history_table.spec.ts` | Пагинация истории (prev/next), переключение периода (Day/Week/Month/Custom), рендер значений. |
| `home/history/history_chart.spec.ts` | График Plotly по окну времени, смена периода, status/background lookup. |
| `home/history/export_csv.spec.ts` | Экспорт истории сенсора в CSV (`ExportHistory`). |
| `notifications/slack_destination.spec.ts` | CRUD Slack-webhook + enable/disable + «send test message». Slack сейчас вообще не тронут (только Telegram во вкладке Chats). |
| `configuration/server_backup_agent.spec.ts` | Вкладки Configuration: Server (порты/TLS), Agent (URL/allow-untrusted/top-CPU), Backup (период/retention/SFTP address+key/«check SFTP»/«create backup now»), SelfMonitoring. |
| `configuration/telegram_bot.spec.ts` | Telegram-бот: токен/name, enable, `RestartTelegramBot`. |
| `home/tree_search.spec.ts` | Поиск/фильтр в дереве (`RefreshTree`/`ApplyFilter`), комбинации фильтров. |
| `home/mute_sensors.spec.ts` | Mute/ignore-notifications модалка, снять mute. |
| `home/edit_sensor_status.spec.ts` | Модалка редактирования статуса/значения сенсора. |
| `home/multi_edit_ttl_alerts.spec.ts` | Multi-edit TTL/алертов на нескольких узлах. |

---

## 🟡 Тир 2 — усилить существующие области негативными/валидационными

Самый дешёвый прирост надёжности.

- **`products/product_validation.spec.ts`** — дубликат имени (409/toast), пустое имя, превышение длины, обязательные поля (Save disabled).
- **`products/folder_validation.spec.ts`** — то же для папок.
- **`dashboard/dashboard_validation.spec.ts`** — дубликат имени, конфигурация панели (источник/агрегация).
- **`alert_templates/template_validation.spec.ts`** — валидация полей, дубликат, **привязка шаблона к сенсору**.
- **`auth/auth_negative.spec.ts`** — пустые поля, блокировка юзера, session-expiry, **logout → возврат на login** (logout сейчас не покрыт), повторный вход.
- **`user_settings/rbac_deny.spec.ts`** — RBAC denial-матрица: viewer не видит Configuration, не создаёт/удаляет продукт/папку/ключ, нет Users-вкладки. Закрывает `test.fixme`-заглушки в `alert_schedules`.
- **`access_key/access_key_negative.spec.ts`** — истечение срока ключа, per-permission отказ, block/unblock, search, server/master ключи.

---

## 🔵 Тир 3 — добить до полного покрытия

- `home/journal.spec.ts` — журнал/audit-log (DataTables grid по узлу).
- `access_key/access_key_search_block.spec.ts` — search, block/unblock из таблицы, server/master ключи.
- `product/agent_download.spec.ts` — кнопка скачивания HSM Agent, проверка zip.
- `product/product_list_filter.spec.ts` — фильтр продуктов по имени/менеджеру.
- `home/file_sensor.spec.ts` — просмотр/скачивание файлового сенсора.
- `alerts/import_export.spec.ts` — экспорт JSON политик + реимпорт (workflow #5 в feature.md).
- `home/db_operations.spec.ts` — Compact / Export значений (admin-only).
- `home/node_ops.spec.ts` — remove node из дерева, clear history, export node CSV, Grafana toggle.

---

## 🟣 Инфраструктура (поднимает надёжность всего сюита)

1. **`data-testid`** — добавить в ключевые контролы, мигрировать хрупкие селекторы. Защищает от всех будущих рефакторингов.
2. **`storageState`** для admin-сессии через `globalSetup` — убирает повторный логин в каждом тесте.
3. **Проекты Firefox/WebKit + мобильный viewport** — сейчас только Chromium.
4. **Почистить дубли**:
   - `dashboard/add_modify_remove_panel.spec.ts` (#9) поглощён `add_remove_modify_dashboard.spec.ts` (#10).
   - `access_key_settings.spec.ts` (#1) дублирует часть `product_edit_allsettings.spec.ts` (#19).
   - Boilerplate создания папки вынести в общий хелпер (повторён 6×).

---

## Рекомендуемый порядок старта

Максимальный прирост реального покрытия за минимальный риск — **Тир 1 + RBAC-matrix из Тир 2**:

1. `home/sensor_alert_editor.spec.ts` — второй путь создания алертов, критичный, полностью непокрытый.
2. `home/history/*` (table + chart + export) — большой операторский UI.
3. `user_settings/rbac_deny.spec.ts` — закрывает дыру безопасности и `test.fixme`.
4. `notifications/slack_destination.spec.ts` + `configuration/*` — целые непокрытые разделы.

## Конвенции при написании (следовать существующему стилю)

- Серийный `test.describe.serial` с разбиением CRUD на отдельные тесты (как `product_add_edit_remove`).
- `uniqueName()` для имён — не хардкодить.
- `afterAll`/`afterEach` cleanup, не падающий (best-effort): API-теardown через `page.request`.
- Фикстура `adminPage` для авто-логина админом.
- Семантические локаторы (`getByRole`/`getByLabel`/`getByText`), по возможности — `data-testid`.
- API для setup (создание сенсора), UI — для проверяемого флоу.

## Ключевые пути для справки

- Спеки: `src/tests/Autotests/`
- Контроллеры: `src/server/HSMServer/Controllers/` (Home, Account, Configuration, Product, Folders, Dashboards, Alerts, AlertTemplates, AlertSchedules, Notifications, AccessKeys, SensorHistory, Journal, Agent, Sensors, Error)
- Views: `src/server/HSMServer/Views/`
- Feature-доки: `aicontext/features/server/{alerts/feature,notifications/feature,agent-download/feature}.md`
