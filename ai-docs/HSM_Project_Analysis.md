# Анализ проекта HSM (Hierarchical Sensor Monitoring)

## 📋 Общее описание

**HSM** — это система мониторинга иерархических сенсоров, построенная на **ASP.NET Core 8.0**. Сервер принимает данные от различных типов датчиков (boolean, integer, double, string, timespan, version, rate, bar-типы, file, enum), хранит их историю, визуализирует через веб-интерфейс и поддерживает интеграцию с Grafana. 

**Версия приложения:** 3.40.17

---

## 🏗️ Архитектура проекта

Проект состоит из **трёх основных компонентов**:

### 1. **HSMCommon** — Общая библиотека утилит
- Потокобезопасные и реактивные коллекции
- Extension-методы для работы с коллекциями, enum, строками, временем
- Базовые классы значений сенсоров (`BaseValue`, `BarBaseValue`)
- Паттерн Result для асинхронных операций (`TaskResult<T>`)
- Утилиты для работы с файлами и хешированием

### 2. **HSMServer.Core** — Ядро бизнес-логики
- Кеширование дерева сенсоров (`TreeValuesCache`)
- Очередь обновлений на базе `System.Threading.Channels`
- Журнал изменений (`JournalService`)
- Расписания алертов (`AlertScheduleParser`, `AlertScheduleProvider`)
- Менеджеры: время, подтверждения, сообщения, расписания
- Модели данных для продуктов, узлов, сенсоров, политик

### 3. **HSMServer** — Основной веб-проект (ASP.NET Core MVC)
- 16 контроллеров (API + MVC)
- Веб-интерфейс с TypeScript + Webpack
- Фоновые сервисы (сбор данных, snapshots, очистка, уведомления, бэкапы)
- Middleware pipeline (логирование, телеметрия, обработка пользователя)
- Аутентификация через cookie с кастомными ролями

---

## 🔧 Используемые технологии

### Backend (.NET 8.0):
| Технология | Назначение |
|---|---|
| **ASP.NET Core MVC** | Веб-фреймворк |
| **Kestrel** | HTTP-сервер (2 порта: 44330 для сенсоров, 44333 для сайта) |
| **FluentValidation** | Валидация моделей |
| **Swagger (Swashbuckle)** | API-документация (`/api/swagger`) |
| **NLog** | Логирование |
| **NodaTime** | Работа с датами/временем |
| **YamlDotNet** | YAML-конфигурации |
| **HtmlSanitizer** | Санитизация HTML |
| **SSH.NET** | SFTP (бэкапы) |
| **Telegram.Bot** | Telegram-уведомления |
| **MemoryPack** | Сериализация |
| **LightningDB** | LMDB-хранилище |
| **HSMDataCollector** | Сбор данных с датчиков (NuGet) |

### Frontend:
| Технология | Назначение |
|---|---|
| **TypeScript 5.3** | Типизированный JS |
| **Webpack 5** | Сборка |
| **jQuery + Bootstrap 5** | UI |
| **Plotly.js** | Графики |
| **DataTables** | Таблицы |
| **Redux Toolkit** | State management |
| **jstree** | Дерево навигации |
| **CodeMirror 6** | Редакторы кода |

---

## 🎯 Архитектурные паттерны

✅ **MVC** — классический ASP.NET Core MVC с Razor-представлениями  
✅ **Dependency Injection** — встроенный DI ASP.NET Core  
✅ **Repository Pattern** — `ConcurrentStorage<T>` (потокобезопасное хранилище с синхронизацией в БД)  
✅ **Background Services** — `IHostedService` для фоновых задач  
✅ **Middleware Pipeline** — кастомные middleware для логирования, телеметрии, аутентификации  
✅ **Result Pattern** — `TaskResult<T>` для обработки ошибок без исключений  
✅ **Factory Pattern** — `DatasourceFactory` для источников данных  
✅ **Observer/Events** — события в хранилищах и кеше  
✅ **Channel-based Queue** — `UpdatesQueue` для асинхронной обработки обновлений сенсоров  
✅ **Authorization Filters** — кастомные фильтры прав доступа

---

## 📁 Конфигурация

- **appsettings.Development.json** — TLS, бэкапы (SFTP), Telegram, порты Kestrel, мониторинг
- **nlog.config** — 3 target-а (ошибки, все сообщения, ASP.NET логи)
- **ServerConfig.cs** — code-first конфигурация из JSON-секций
- **Webpack** — dev/prod конфигурации для frontend

---

## 🔐 Аутентификация и авторизация

- Cookie-based аутентификация
- Кастомный `UserManager` с ролями (Viewer, Manager)
- Фильтры прав на чтение/запись данных
- Ролевые фильтры для продуктов, папок, Telegram

---

## 🗄️ База данных

- **HSMDatabase** (внешний проект) — основной уровень данных
- **HSMDatabase.LevelDB** — LevelDB/LMDB хранилище
- **Сегментация данных** по временным интервалам (недельные сегменты)
- Папки: `ServerLayout/`, `Journals/`, `SensorValuesV2_*/`, `Snapshots/`

---

## 🌐 Интеграции

- **Grafana** — дашборды и datasource-ы для визуализации
- **Telegram** — бот для уведомлений
- **SFTP** — бэкапы на удалённый сервер
- **Email** — отправка email-уведомлений

---

## ⚠️ Замечания

❌ **Отсутствуют тесты** — в проекте нет unit/integration тестов  
⚠️ **Внешние зависимости** — HSMDatabase и HSMDataCollector как внешние проекты/NuGet пакеты  
⚠️ **Сложная архитектура** — много уровней абстракции, может быть сложно для новых разработчиков

---

## 📊 Итоговая оценка

| Параметр | Оценка |
|---|---|
| **Архитектура** | ✅ Хорошая (многослойная, DI, паттерны) |
| **Технологии** | ✅ Современные (.NET 8, TS 5.3, Webpack 5) |
| **Документация** | ⚠️ Swagger есть, README отсутствует |
| **Тестирование** | ❌ Отсутствует |
| **CI/CD** | ⚠️ Docker поддержка через SDK, но нет Dockerfile |
| **Безопасность** | ✅ HtmlSanitizer, cookie auth, фильтры прав |

---

## 📂 Структура проекта

```
c:\Git\HSM\Hierarchical-Sensor-Monitoring\src\server\
├── HSMCommon/                        # Общая библиотека утилит
│   ├── Collections/Concurrent/       # Потокобезопасные коллекции
│   ├── Collections/Reactive/         # Реактивные коллекции
│   ├── Constants/                    # Константы
│   ├── Extensions/                   # Extension-методы
│   ├── SensorValues/                 # Базовые типы значений сенсоров
│   ├── TaskResult/                   # Result-паттерн
│   └── Threading/                    # Утилиты многопоточности
│
├── HSMServer.Core/                   # Ядро бизнес-логики
│   ├── AlertSchedule/                # Расписания алертов
│   ├── Cache/                        # Кеширование дерева значений
│   ├── Extensions/                   # Конвертеры и extension-методы
│   ├── Journal/                      # Журнал изменений
│   ├── Managers/                     # Менеджеры (время, подтверждения, расписания)
│   ├── Model/                        # Модели данных
│   ├── PathTemplates/                # Шаблоны путей
│   ├── Services/                     # HtmlSanitizerService
│   ├── StatisticInfo/                # Статистика истории
│   ├── TableOfChanges/               # Таблица изменений
│   ├── Threading/                    # PeriodicTask
│   ├── TreeStateSnapshot/            # Снимки состояния дерева
│   └── UpdatesQueue/                 # Очередь обновлений (Channels)
│
└── HSMServer/                        # Основной веб-проект (ASP.NET Core 8 MVC)
    ├── ApiObjectsConverters/         # Конвертеры API-объектов
    ├── Attributes/                   # Кастомные атрибуты
    ├── Authentication/               # IUserManager, UserManager
    ├── BackgroundServices/           # Фоновые сервисы
    ├── ConcurrentStorage/            # ConcurrentStorage<T> (Repository)
    ├── Config/                       # appsettings.Development.json
    ├── Constants/                    # Константы сервера
    ├── Controllers/                  # 16 MVC/API контроллеров
    ├── Dashboards/                   # Управление дашбордами
    ├── Databases/                    # Сегментированные данные
    ├── Datasources/                  # DatasourceFactory, источники данных
    ├── DTOs/                         # DTO для API
    ├── Email/                        # EmailSender
    ├── Extensions/                   # Extension-методы (18 файлов)
    ├── Filters/                      # Авторизационные фильтры
    ├── Folders/                      # IFolderManager, FolderManager
    ├── Helpers/                      # Вспомогательные классы
    ├── JsonConverters/               # JSON-конвертеры
    ├── Middleware/                   # Custom middleware
    ├── Model/                        # ViewModels (16 поддиректорий)
    ├── ModelBinders/                 # Кастомные модель-байндеры
    ├── Notifications/                # NotificationCenter, Telegram
    ├── ObsoleteUnitedSensorValue/    # Устаревший код
    ├── ServerConfiguration/          # ServerConfig, секции
    ├── Sftp/                         # SFTP-клиент
    ├── TagHelpers/                   # Razor TagHelpers
    ├── Validation/                   # FluentValidation
    ├── Views/                        # Razor-представления
    ├── wwwroot/                      # Статические файлы (JS/CSS)
    ├── Program.cs                    # Точка входа
    ├── nlog.config                   # Конфигурация логирования
    ├── package.json                  # npm-зависимости
    └── webpack.*.js                  # Webpack-конфигурация
```

---

## 🔗 Зависимости между проектами

```
HSMServer (ASP.NET Core Web)
    |
    +---> HSMCommon (Utils, базовые типы)
    |
    +---> HSMServer.Core (бизнес-логика)
    |         |
    |         +---> HSMCommon
    |         +---> HSMDatabase.LevelDB
    |
    +---> HSMDatabase (внешний, уровень данных)
    +---> HSMDataCollector (NuGet, сбор данных с датчиков)
    +---> HSMSensorDataObjects (NuGet, DTO сенсоров)
```

---

**Дата анализа:** 13 апреля 2026 г.
