# Docker And Runtime Configuration

> Owner: shared | Last reviewed: 2026-05-28 | Canonical: yes

## Scope

This document tracks runtime assumptions for Docker, local startup, native
dependencies, and configuration. Keep it generic; put deployment-environment
specific secrets or hostnames elsewhere.

## Files To Check

- `docker-compose.yml`
- `docker_scripts/`
- project Dockerfiles
- `nlog.config` / `collector.nlog.config`
- native library paths under `src/lib/`
- app/server configuration classes

## Review Checklist

- Configuration has safe defaults or clear required variables.
- Secrets are not committed.
- Native dependencies exist for supported platforms.
- Startup and shutdown behavior is compatible with long-running services.
- Partial deployment or version mismatch does not silently corrupt data.
- Logs identify config/runtime failures clearly.
