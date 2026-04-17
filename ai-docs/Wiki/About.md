# Welcome to the Hierarchical-Sensor-Monitoring wiki!

> **Note:** This page is kept for historical reference. For the most up-to-date documentation, see [Home](Home).

**Hierarchical Sensor Monitoring (HSM)** is a self-hosted monitoring platform for collecting sensor data from your services and applications, with a powerful and flexible alert system.

**Program features:**
* Collection of information about data streams passing through specific devices, saving it in the program database
* Data collection via HTTPS protocol (REST API or [HSMDataCollector NuGet package](HSMDataCollector))
* Viewing statistics in the database in the form of graphs and tables
* Statistics of transmitted data packets and ping time
* Viewing results in real time or for a certain period of time in the past on different devices
* Collection of data on the load on the memory subsystems and the processor
* Telegram notifications when alert conditions are met
* Grafana integration for external dashboards

The results can be displayed through the program's own graphical interface (Web UI) or in an Internet browser. In addition, a web server is integrated into the program, which allows you to receive data using a remote connection, and the built-in authentication system allows you to monitor the results in multi-user mode. An example of work is below:

Aggregator - a connected product to the HSM system. In this example, we are tracking the connection Aggregator - Liquidity Provider (LP). The aggregator sends the current state to the HSM server, which, in turn, sends this data to the site, for visualization convenience, and to Telegram, to notify customers about the current situation on the product.
![About1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About1.PNG)

At some point, communication with LP was interrupted. The aggregator reports this to the HSM server.
![About2](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About2.PNG)

HSM perceives this as an erroneous situation, changes the status of the product on the site, and sends a notification to the user that the connection with LP is broken.
![About3](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About3.PNG)

The LP connection error will last until the connection is restored and the aggregator sends information about it. As soon as this happens, the HSM server will update the data on the site and send a telegram notification that the connection with LP has been restored.
![About4](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/.github/Screens/About/About4.PNG)

---

## Quick Navigation

| I want to... | Go to |
|---|---|
| Learn how HSM works | [Getting Started](Getting-Started) |
| Install the server | [Installation](Installation) |
| Send my first sensor value | [Quick Start](Quick-start) |
| Set up alert notifications | [Alerts Overview](Alerts-Overview) |
| Connect Telegram | [Telegram Setup](Telegram-Setup) |
| Use the NuGet library | [HSM DataCollector](HSMDataCollector) |
| Send data via HTTP | [REST API](REST-API) |
| Integrate with Grafana | [Grafana Integration](Integration-with-Grafana) |
| See all sensor types | [Sensor Types](Sensor-types) |
| View the glossary | [Glossary](Glossary) |

---

## See Also

- [Home](Home) — Main documentation entry point
- [Getting Started](Getting-Started) — How HSM works and key concepts
- [Core Concepts](Core-Concepts) — Sensors, Products, Folders, Access Keys
- [HSM Architecture](HSM-architecture) — System architecture overview
