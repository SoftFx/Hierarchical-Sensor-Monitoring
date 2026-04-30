# Users

The **Users** window allows admins to view a list of all registered users, add new users, and edit existing HSM users.

See also: [Registration](Registration), [Glossary](Glossary), [Site Structure](Site-structure)

---

## Overview

The window contains a table divided into two parts:
* Adding a new user
* Table of already existing users

![AllUsers](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/e31140f2-3bb7-419e-9fd2-1950c198b6a8)

## Columns

The whole table consists of 5 columns:

| Column | Description |
|---|---|
| **Username** | Login, which is set by the user/admin during registration |
| **Password** | The password that the user/admin sets during registration |
| **IsAdmin** | A checkbox that determines whether the given user is an admin |
| **Products** | A list of all products to which the user is connected as ProductManager/Viewer |
| **Action** | A list of actions that can be performed on the user |

### Actions

* **Delete user** — permanently removes the user
* **Edit user** — at the moment only adding/removing admin rights is possible
* **Confirmation/rejection of editing** — approve or reject changes

## Adding a User

To add a user, the admin needs to set the Username, Password, add admin rights (if necessary) and click the "+" button.

![2CreateUser](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/df98368f-8b48-44aa-9976-a253fc86078a)

The list of products and the role (ProductManager/Viewer) is defined within each project. Through the user window there is no way to change this.

![3Products](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/12e4faf2-bdc1-44eb-8875-f25f31d00017)

---

## See Also

- [Registration](Registration) — How users register and get their first role
- [Glossary → Admin](Glossary) — Admin role definition
- [Glossary → Product Manager](Glossary) — Product Manager role definition
- [Glossary → Product Viewer](Glossary) — Product Viewer role definition
- [Site Structure → Users tab](Site-structure#users-tab) — Users tab in the site structure

