# Jira MCP Server — Настройка и Информация

## Jira Server

- **URL**: `https://intranet.soft-fx.lv/jira`
- **Версия**: Jira 6.4 (on-premise)
- **REST API**: `https://intranet.soft-fx.lv/jira/rest/api/2`
- **Проект HSM в Jira**: **TAM** (TickTrader AI Monitoring)
- **Ключ задач**: `TAM-XXXX`

## Аутентификация

- **Пользователь**: peter.vasiliev
- **Метод**: Basic Auth (username:password)

## MCP Server Configuration

### Расположение сервера
- **Код сервера**: `C:\Git\HSM\Hierarchical-Sensor-Monitoring\.vscode\jira-mcp\server.js`
- **Конфиг Qwen Code**: `C:\Git\HSM\Hierarchical-Sensor-Monitoring\.qwen\settings.json`
- **Зависимости**: `C:\Git\HSM\Hierarchical-Sensor-Monitoring\.vscode\jira-mcp\package.json`

### .qwen/settings.json

```json
{
  "mcpServers": {
    "jira": {
      "command": "node",
      "args": [
        "C:\\Git\\HSM\\Hierarchical-Sensor-Monitoring\\.vscode\\jira-mcp\\server.js"
      ],
      "env": {
        "JIRA_URL": "https://intranet.soft-fx.lv/jira",
        "JIRA_USERNAME": "peter.vasiliev",
        "JIRA_PASSWORD": "Retep1973",
        "NODE_TLS_REJECT_UNAUTHORIZED": "0"
      }
    }
  },
  "$version": 3
}
```

### Зависимости (package.json)

```json
{
  "dependencies": {
    "@modelcontextprotocol/sdk": "^1.29.0",
    "dotenv": "^17.4.2",
    "zod": "^3.25.76"
  }
}
```

## Известные проблемы и решения

### 1. SSL Certificate Error
**Проблема**: `fetch failed (cause: unable to verify the first certificate)`
**Решение**: 
- Установить `NODE_TLS_REJECT_UNAUTHORIZED=0` в env MCP-сервера
- При использовании curl добавить флаг `-k`

### 2. StdioTransport Import Error (SDK v1.x)
**Проблема**: `The requested module does not provide an export named 'StdioTransport'`
**Решение**: В SDK v1.x класс переименован в `StdioServerTransport`:
```javascript
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
```

### 3. registerTool API в SDK v1.x
**Проблема**: `inputSchema must be a Zod schema or raw shape`
**Решение**: В SDK v1.x `registerTool` принимает Zod-схему в `parameters`:
```javascript
server.registerTool(
  "tool_name",
  {
    description: "Tool description",
    parameters: z.object({
      param1: z.string().describe("Parameter description"),
    }),
  },
  async ({ param1 }) => { /* implementation */ }
);
```

### 4. JQL Query Syntax (Jira v6.4)
**Проблема**: Jira v6.4 может не поддерживать современный JQL синтаксис.
**Решение**: Использовать простой формат `field=value`, например `project=TAM`.

## Доступные MCP Инструменты

| Инструмент | Описание | Параметры |
|------------|----------|-----------|
| `search_issues` | Поиск задач через JQL | `jql`, `maxResults?`, `startAt?` |
| `get_issue` | Получить детали задачи | `issueKey` |
| `create_issue` | Создать новую задачу | `projectKey`, `summary`, `issueType`, `description?`, `assignee?`, `labels?` |
| `update_issue` | Обновить задачу | `issueKey`, `summary?`, `description?`, `labels?` |
| `add_comment` | Добавить комментарий | `issueKey`, `body` |
| `transition_issue` | Изменить статус задачи | `issueKey`, `transitionId`, `comment?` |
| `get_transitions` | Получить доступные переходы | `issueKey` |
| `get_project` | Информация о проекте | `projectKey` |
| `list_projects` | Список всех проектов | — |

## Доступные проекты в Jira

| Ключ | Название |
|------|----------|
| TAM | TickTrader AI Monitoring |
| TP | TickTrader Server |
| TTA | TickTrader Administration |
| TPAC | TickTrader Admin |
| TAT | TickTrader Admin Tools |
| AE | TickTrader AdminEye |
| AGREG | TickTrader Aggregator |
| AT | TickTrader Autotesting |
| DVCONSOLE | TickTrader DeveloperConsole |
| FDKPRO | TickTrader FDK |
| MA | TickTrader Terminal (Android) |
| MTI | TickTrader Terminal (iOS) |
| FXTRADER | TickTrader Terminal (Win) |
| TRAIDTOOL | TickTrader Trading Tools |
| DESBA | TT Terminals Design / BA |
| RISK | TT Terminals Risks |
| MAMS | Machine Advanced Monitoring System |
| MON | Monevium.com |
| FXOEU | FXOpen EU |
| CABB | Cabinet Back |
| CBR | Cabinet Business Requirements |
| FIP | F2X IT Platform |
| INT | Internal |
| OM | Office management |
| QA | QA planning |
| REP | Reporting |
| TA | Technical Administration |

## Ручная проверка MCP сервера

### Запуск сервера:
```cmd
set NODE_TLS_REJECT_UNAUTHORIZED=0 && node C:\Git\HSM\Hierarchical-Sensor-Monitoring\.vscode\jira-mcp\server.js
```
Ожидаемый вывод: `Jira MCP Server started`

### Проверка через curl:
```cmd
curl -k -u peter.vasiliev:Retep1973 "https://intranet.soft-fx.lv/jira/rest/api/2/issue/TAM-2040"
```

### Поиск задач через REST API:
```cmd
curl -k -s -u peter.vasiliev:Retep1973 "https://intranet.soft-fx.lv/jira/rest/api/2/search?jql=project%3DTAM&maxResults=3"
```

## Дата настройки
- **Настроено**: 14 апреля 2026
- **Настроил**: Peter Vasiliev с помощью Qwen Code
