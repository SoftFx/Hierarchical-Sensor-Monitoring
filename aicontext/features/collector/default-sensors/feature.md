# Feature: Default Sensors

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes
> Scope: Collector - built-in system and process monitoring sensors

---

## Description

DataCollector provides pre-built sensors for common system metrics. Users enable them through `Windows` or `Unix` collection interfaces. Default sensors are organized by platform (Windows-only, Unix-only, cross-platform).

---

## Available Sensors

### Cross-platform
- Process CPU usage
- Process memory
- Process thread count / ThreadPool thread count
- Process GC stats
- Collector alive heartbeat
- Collector errors (diagnostic)
- Free disk space + disk space prediction

### Windows-only
- Total system CPU (Performance Counters)
- Free RAM
- Windows service status monitoring
- Windows Update status

### Unix-only
- System CPU (from `/proc/stat`)
- Free RAM (from `/proc/meminfo`)

---

## Key Subsystems

### Windows Service Status Sensor
Monitors a named Windows service via `ServiceController`.
- Resolves service on startup and after fault; disposes unmatched `ServiceController` instances
- Fault state delay: 1 hour between re-resolve attempts (avoids hammering `GetServices()`)
- `_nextServiceResolveTime` throttles resolution without blocking the timer thread (replaces the old `Task.Delay` approach)
- Disposes `ServiceController` on stop and on fault

### Free Disk Space Prediction
Estimates time until disk fills up based on speed of space consumption.
- Calibration period: first N requests collect baseline data
- Speed calculation: exponential moving average (0.9 old + 0.1 new)
- `Interlocked.Exchange` for thread-safe speed update

### Bash Command Execution (Unix sensors)
`BashCommandExtension.BashExecute()` runs shell commands with:
- 5-second timeout with process kill on timeout
- stderr capture alongside stdout
- Non-zero exit code throws `InvalidOperationException`

---

## Key Files

| File | Purpose |
|---|---|
| `DefaultSensors/Windows/Service/WindowsServiceStatusSensor.cs` | Windows service monitoring |
| `DefaultSensors/BaseTemplates/FreeDiskSpacePredictionBase.cs` | Disk prediction with speed calibration |
| `DefaultSensors/SystemInfo/ProcessInfo.cs` | Process creation for bash/powershell commands |
| `Extensions/BashCommandExtension.cs` | Shell execution with timeout |
