# Glossary

This page defines the key terms used throughout the HSM documentation.

> For a more detailed explanation of these concepts, see [Core Concepts](Core-Concepts).

---

## Terms

**Product** — a logical group that represents a physical computer or a group of computers. Every product must have a unique name. Every product is given a unique [Access Key](Access-keys), which is used to send data corresponding to that particular product. In the client app, the product specifies the tree root.

See also: [Products tab](Site-structure#products-tab), [Core Concepts → Product](Core-Concepts#product)

**Sensor** — an entity that transmits its state to the server. The sensor is defined by its name and path in the product tree. Sensors have different [types](Sensor-types) such as Bool, Int, Double, String, etc.

See also: [Core Concepts → Sensor](Core-Concepts#sensor), [Sensor Types](Sensor-types)

**Access Key** (formerly Product Key) — a character string given to a product during creation. Access keys are used to authenticate sensor data and distinguish the product. Keys can have different permissions (send data, read data, add nodes, add sensors).

See also: [Access Keys](Access-keys), [Core Concepts → Access Key Permissions](Core-Concepts#access-key-permissions)

**Node** (also called **Folder**) — a subgroup for collecting sensors together, which is smaller than a product and is presented as a node in the visual tree. Folders are used to group products and connect Telegram chats.

See also: [Core Concepts → Folder](Core-Concepts#folder), [Site Structure → Folders](Site-structure#folders)

**Admin** — the role of a user who has all possible access. Admin can view data from all existing products, manage all products, and may access the [Configuration](Configuration) page, where server parameters can be edited. Admins can also manage [Users](Users).

See also: [Users](Users), [Registration](Registration)

**Product Manager** — a role inside a product that grants access to edit the product page, where the manager can add and/or invite new viewers and managers via email, and add new extra access keys.

See also: [Site Structure → Product Editing](Site-structure#product-editing)

**Product Viewer** — the simplest role, which allows the user to see the product in the monitoring tree and on products page, but does not give access to edit the product page.

**TTL (Time-To-Live)** — a timeout after which a sensor is considered inactive. If no value arrives within the TTL window, the sensor status changes to **OffTime**.

See also: [Core Concepts → TTL](Core-Concepts#ttl-time-to-live), [Alerts Overview → TTL Alerts](Alerts-Overview#ttl-alerts)

**Alert Policy** — a rule on a sensor that defines when to send a notification. Policies consist of conditions, destinations, templates, and schedules.

See also: [Alerts Overview](Alerts-Overview), [Alert Conditions Reference](Alert-Conditions-Reference), [Alert Templates](Alert-Templates)

---

## See Also

- [Core Concepts](Core-Concepts) — Detailed explanation of all HSM building blocks
- [Site Structure](Site-structure) — How the HSM web UI is organized
- [Home](Home) — Main documentation entry point