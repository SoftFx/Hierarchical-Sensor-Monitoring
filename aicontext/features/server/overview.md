# Server Features Overview

> Owner: server | Last reviewed: 2026-05-28 | Canonical: yes

The server area owns the ASP.NET HSM Server, server core workflows, background
services, authentication, dashboards, alerts, notifications, and operational
configuration.

## Feature Folders To Add Here

- `ingestion/` - receiving and validating collector data.
- `alerts/` - alert conditions, templates, schedules, notification triggers.
- `notifications/` - Telegram/email delivery, retries, failure handling.
- `dashboards/` - server-owned dashboard behavior and data shaping.
- `auth/` - authentication, access keys, users, permissions.
- `background-services/` - hosted services, queue workers, startup/shutdown.

Create folders from `../_TEMPLATE_feature.md` as work lands.
