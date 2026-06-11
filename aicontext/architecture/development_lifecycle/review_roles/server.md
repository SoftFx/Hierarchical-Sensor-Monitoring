# Server And API Review Roles

## Server/API Reviewer

Focus:

- ASP.NET controllers, model binding, filters, middleware, DTO conversion, and response semantics;
- sensor ingestion, history queries, dashboards, folders, datasources, alerts, and notification workflows;
- background services, update queues, cache invalidation, and long-running server processes;
- compatibility between server contracts and collector/client DTOs.

Must read:

- changed files under `src/server/HSMServer`, `src/server/HSMServer.Core`, and `src/api/HSMSensorDataObjects`;
- affected controller actions, services/managers, DTO converters, and validation paths;
- existing tests under `src/tests/HSMServer.Core.Tests` and Playwright tests under `src/tests/Autotests` when relevant.

Output:

- confirmed endpoint/service behavior bugs with request path and affected audience;
- contract mismatches between API objects, converters, server model, and docs;
- missing tests for validation, happy path, error path, and compatibility;
- risks around cache invalidation, background queues, and partial failures.

---

## Security / Access Reviewer

Focus:

- authentication and authorization under `Authentication`, filters, access keys, user permissions, and environment isolation;
- unauthorized data exposure through dashboards, sensor paths, history, alerts, files, or Swagger/API responses;
- unsafe batch operations, destructive actions, and configuration endpoints;
- secrets, tokens, Telegram/email settings, SFTP credentials, and logs.

Must read:

- changed auth/filter/controller/configuration files;
- request DTOs and API docs for changed endpoints;
- tests around auth, access, user registration, and environment scoping when present.

Output:

- authorization bypass or data leakage risks;
- missing forbidden/unauthorized tests;
- sensitive value exposure in payloads, logs, UI, or docs;
- recommendations for least-privilege and safe error messages.

---

## Alerts / Notifications Reviewer

Focus:

- alert conditions, schedules, templates, Telegram/email notification routing, and debounce/dedup behavior;
- sensor state transitions that trigger alerts;
- time zone and schedule edge cases;
- operational clarity when alerts are suppressed, disabled, delayed, or failed.

Must read:

- changed alert, schedule, notification, email, Telegram, and sensor-status paths;
- alert-related docs and UI screens;
- tests covering alert condition evaluation and schedule behavior.

Output:

- missed or duplicate alert risks;
- time-window and schedule bugs;
- missing tests for boundary conditions, disabled states, and failure handling;
- observability gaps for notification delivery.
