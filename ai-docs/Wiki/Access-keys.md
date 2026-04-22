# Access Keys

The **Access Keys** window contains a list of all access keys on the site. Access keys are used to authenticate API requests and control permissions for sending and reading sensor data.

See also: [Core Concepts → Access Key Permissions](Core-Concepts#access-key-permissions), [Site Structure → Access Keys](Site-structure#access-keys)

---

## Overview

![image](https://user-images.githubusercontent.com/43994777/232472360-5f2503d0-f51c-4958-9624-9ba074b6922c.png)

Access keys are very similar in structure to the Products window. Structurally, the site consists of a list of keys and a search string.

The search is conducted by the key — you can use only part of the key, after which all information on it will be found.

![image](https://user-images.githubusercontent.com/43994777/232474571-57c83f86-f6de-414b-a16a-10b0fadbdba4.png)

## Key List Columns

The list of keys consists of 7 elements:

| Column | Description |
|---|---|
| **Product** | The name of the product this key belongs to |
| **Name** | The name of the key that the user specifies when creating it. The "Default" key is created when the product is created |
| **Key** | The actual access key value |
| **Last Update** | Displays the time when the product data was last updated |
| **Permissions** | Allow you to edit or delete a product |
| **Author** | Contains information about all permissions that the key has |
| **State** | Displays the status of the key |
| **Actions** | A list of actions that can be performed with a key |

### Permissions

| Permission | Description |
|---|---|
| **CanSendSensorData** | The user will be able to send sensor data using this key |
| **CanReadSensorData** | The user will be able to read sensor data using this key |
| **CanAddNodes** | The user will be able to create nodes for this product |
| **CanAddSensors** | The user will be able to create sensors for this product |
| **Full** | Includes all permissions described above |

### Key States

| State | Icon | Description |
|---|---|---|
| **Active** | ![image](https://user-images.githubusercontent.com/43994777/231736874-77570252-28d8-4f78-94d4-ee86649fe723.png) | This key is available for work, in accordance with its permissions |
| **Expired** | ![image](https://user-images.githubusercontent.com/43994777/231736905-8284479c-2f90-4998-9735-f77614756fb6.png) | This key has expired |
| **Blocked** | ![image](https://user-images.githubusercontent.com/43994777/231736957-9c738ce1-4e73-4455-bae1-9e481db56662.png) | The key was manually blocked |

### Actions

| Action | Description |
|---|---|
| **Edit** | Opens the key editing form |
| **Block/Unblock** | Manually blocks or unblocks the key; the operations of the corresponding permissions are blocked until the Manager/Admin unblocks the key |
| **Delete** | Deletes the key |

## Editing a Key

![EditKey](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/616bd2e5-77b2-4363-a79a-29ea3f2855db)

In this form, the user specifies:
* **Display name** — display name for the key
* **Permissions** — all permissions that the key has, the description of each was above
* **Expiration** — expiration date of this key (set when creating a key, cannot be edited)

---

## See Also

- [Core Concepts → Access Key Permissions](Core-Concepts#access-key-permissions) — Permission levels explained
- [Site Structure → Access Keys](Site-structure#access-keys) — Access keys in the site structure
- [Integration with Grafana](Integration-with-Grafana) — How to use access keys for Grafana integration
- [REST API → Authentication](REST-API#authentication) — Using keys in API requests
