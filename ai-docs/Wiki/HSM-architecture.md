# HSM Architecture

This page describes the internal architecture of the Hierarchical Sensor Monitoring system.

---

## Overview

![MainScheme](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Architecrute/Arch1.png)

Main modules:

* **HSM Server** — the main service of the project, is the end point for user data. Serves for processing, validating and storing data
* **HSM objects** — API of the product, contains a description of what form the values should come to the HSM Server for correct processing
* **DataCollector** — NuGet package with classes for correct requests to the server and various sensor implementations for the computer
* **Wrapper** — a DataCollector wrapper written in C++ to be used in non-.NET projects

![MainScheme](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Architecrute/Arch4.png)

---

## Server Components

* **HSMServer** — Web server for receiving data from clients and managing client sites
* **HSMServer.Core** — Library for storing and recalculating the global state of the sensor tree
* **HSMServer.Core.Monitoring** — Library for monitoring HSM server health

---

## Database Layer

* **HSMDatabase** — Database manager, serves as a facade for querying the database
* **HSMDatabase.AccessManager** — Database entity format protocol, needed to isolate the database implementation
* **HSMDatabase.LevelDB** — Binary NoSQL database format, isolated from the general assembly and works through the AccessManager

### LevelDB Databases

| Database | Description |
|---|---|
| **EnvironmentData** | Meta-information about the HSM structure (users, products, sensors, keys, filters, settings, etc.) |
| **SensorValues** | Lightweight sensor value database (introduced after Sprint 8) |
| **MonitoringData** | Old format database (write disabled after Sprint 8, read disabled after Sprint 12) |

![MainScheme](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/Architecrute/Arch5.png)

---

## See Also

- [Home](Home) — Main documentation entry point
- [Server Configuration](Server-Configuration) — How to configure the HSM server
- [HSM Server Monitoring](HSM-Server-Monitoring) — Self-monitoring sensors collected by the server
- [HSM DataCollector](HSMDataCollector) — NuGet package for sending data from .NET applications
- [REST API](REST-API) — HTTP API for non-.NET applications