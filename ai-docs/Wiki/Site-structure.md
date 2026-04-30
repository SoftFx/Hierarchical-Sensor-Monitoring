# Site Structure

This page describes the structure of the HSM web interface, including the tree view, products, access keys, users, and configuration tabs.

See also: [Using HSM Site](Using-HSM-Site), [Components of HSM Site](Components-of-HSM-Site), [Home](Home)

---

The HSM site is the main product for visualizing data coming to the server. It has the following structure:
* [Tree](#tree);
    - [Work with tree](#work-with-tree);
    - [Data filtering](#data-filtering);
    - [Graph and data table](#graph-and-data-table);
* [Products tab](#products-tab);
    - [Folders](#folders);
    - [Products](#products);
    - [Examples of connecting external services](#examples-of-connecting-external-services);
* [Access keys tab](Access-keys);
* [Users tab](Users);
* [Configuration tab](Configuration);
* API.

Let's take a look at each of the blocks below.

## Tree
### Work with Tree
A tree structure is used to present information about products connected by customers to the HSM site. A tree structure is one way of representing a hierarchical structure in a graphical way. It is so called due to the fact that it visually looks like an upside down tree. Tree example below:

![1Tree](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/62b30eb4-59f3-4f14-bc11-c7e7700bf231)

The tree structure displays all products, nodes and sensors. Each of the above has 4 possible statuses (the order of the statuses, displays their priority, from lowest to highest, from top to bottom):
* OffTime (
![image](https://user-images.githubusercontent.com/43994777/220883375-586c9f6c-f30a-4363-ac96-91216fffebb0.png)) - no data available due to schedule settings on the client or server application;
* Ok (
![image](https://user-images.githubusercontent.com/43994777/220883498-07d6695a-b863-4637-9fb8-44363e23706e.png)) - the data received by the sensor corresponds to the expected result;
* Error (
![image](https://user-images.githubusercontent.com/43994777/220883712-79809a2e-8a02-41ed-8026-f0d1dd71e173.png)) - the data received by the sensor contains an error.

The user has the ability to set up alerts, ignore the sensor, edit product metadata by right-clicking on the product.

![2ContextMenu](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/85d203cc-c324-46d7-9977-b39fbb1f5f06)

The list of parameters available to the user depends on the rights granted to him on the site. Rights and accesses will be described in more detail in the Roles section on the site.

### Data filtering

For the convenience of working with the tree, filters are used that the user configures personally for himself. To call the filtering settings, click on the “Filters” button. The filter settings form appears as a pop-up window to the left of the tree:

![3Data filtering](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/d9870673-165c-4ece-b6f8-f086eb362270)

Possible filtering settings:
1. **Update tree interval** - time for updating statuses in the tree. Default = 5 seconds. Example: Update tree interval = 5 seconds. After updating the sensor status on the server, within 5 seconds the new status will be displayed on the tree.
1. **Status** - display sensors with the selected status on the tree. In this case, if the selected status is “lower” in priority than the one on the tree, then the status “higher” in priority will be indicated in the products and nodes. Example:

![4StatusFilter](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/d67f988b-7c75-4bd1-89c8-5f34fe1a6da9)

The tree is filtered to show all products, nodes and sensors with the status “Ok”, however there are sensors in the Bitmakerts Live product and node Feed Connections with the status “Error”. In this case, the user sees the nodes with the “Ok” status, and those products and nodes where there is an error is shown with the “Error” status, and the sensors that caused the error are not displayed directly. This is done so that in the case when the user monitors the system on the “Ok” status, there will be no error skipping situation.

3. **History** - this filter is used to add to the list the display of products for which there is no information. Either it did not come, or all data was cleared / deleted. !Not supported!

4. **Notification** - !Not supported! - allows you to leave in the tree display sensors that have enabled 
5. **Blocked** -!Not supported!- includes all products that have been blocked in the display. (This functionality is being removed at the moment).
6. **Sort By** - used to define the sort order:
   - **By Name** (
![image](https://user-images.githubusercontent.com/43994777/220891042-de165d85-b6d0-4f65-87e2-8161c7bff641.png)) - alphabetically, from A to Z. Used by default;
   - **By Last Update** (
![image](https://user-images.githubusercontent.com/43994777/220891288-e1453807-7222-4084-a36b-3079d1d69664.png)) - by the time of the last update. That product will be higher, whose update time is closer to the current one. Example, at the given time 17:00, there are 3 products in the list of products: Product 1 (16:50), Product 2 (16:13), Product 3 (16:57). When using sorting by last update time, the order in which products will be displayed in the tree will be as follows (from top to bottom): Product 3, Product 1, Product 2.
7. **Apply** - This button is used to confirm and apply the filter option that the user has selected.

After the filter is applied, tooltips will appear that indicate how the tree is currently filtered and the number of filters applied. Example below:

![5FilterApplied](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/7971af80-9c29-437f-975d-fa07a299a2ec)


In this example, we see that the tree is filtered by 2 filters, by status and by visibility, with the value Icons. The order of the products on the tree is by the name.

### Graph and data table

To the right of the tree, the data obtained by the product is displayed using a graph or table. The display depends on whether the node or product has its own sub-products/sensors.
If the node contains sub-products, then to the right of the tree information about:
* Product name;
* The current setting of the Expected Update interval parameter (can be edited);
* Nodes / sub-products and their states in the form of Grid or List.

The state of the nodes in the form of a grid:

![6GraphAndDataTable1](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/6ec249eb-d8ad-49b4-bb19-6f9cbc540d08)

The state of the nodes in the form of a list:

![7GraphAndDataTable2](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/ff98e3ed-74f3-44d5-abae-bfb6e2987cfe)

1. If the selected element on the tree is a sensor (node ​​without subproducts), then to the right of the tree information about:
1. Meta information of the sensor, which includes (to view, you need to click on the text “Show meta info”):
   - Parent product (Product);
   - The path (Path) along which this sensor lies;
   - Sensor type;
   - Expected update Time - set by default from the parent, i.e. node one level up. In this case, next to the parameter there is a mark (from parent).
   - Description
   - Unit of measurement (Unit).
1. Brief information about the sensor:
   - Status;
   - The latest data;
   - The time of the last update.
1. Time period for which the data is displayed;
1. Number of bars (**only for sensors that collect information in bars**);
1. Data collected by the sensor in the form of a Graph or a Table. By default, the number of entries to display = 100.

![image](https://user-images.githubusercontent.com/43994777/220901391-6c872893-9587-4fbd-9042-a9ad84308df1.png)

***

## Products tab

The products tab is required to create/edit/delete and view folders/products added by customers on HSM. After opening the tab, the user will see a list of folders/products available to him on the project.

![ProductsTabView](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/148eebf4-929c-4e2a-b715-094ddba688bf)

### Folders

If the user has enough rights, then he can add folder by link "+Add folder" on Products tab. To create a folder you need to specify its name (description) and click the "Save" button. After that, the user will be redirected to the "Edit folder" page, where he can configure it.
To delete a folder click "Remove" link. In case containing products by folder, all products from this folder will be moved to "Other products" section (products without folder).
Let's look at the folder settings that user could configure. On _General_ tab we see name, description, color, author, creation date  information and connected to this folder products list. New products could be added via "+Add product(s)" link. To exclude product select "Exclude from folder" option in "Action" column. 

![01Folder_General](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/f9fffd8b-d3f3-413a-a429-f3799226533b)

On _Settings_ tab user could set "Keep sensors history". The period during which the sensor history will be stored. After this period has expired, the history will be clean up. 
"Remove sensors after inactivity" defines the period after which the sensor will be deleted by the system. This settings applied only for sensors. Empty nodes user have to delete manually. Default values for "Keep sensors history" and "Remove sensors after inactivity" settings is 1 month.
Parameter "Time to sensors live" used for TTL alert. These settings will be used as parent when setting up TTL alert. Default value is Never.

![02Folder_Settings](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/36666398-356c-4f75-ac29-76cd6580e7aa)

_'Telegram'_ tab provides information about telegram chats connected to the folder. User could add chat connected to HSM via "Choose chats to add" list or add new chat via link "+Add new chat". Guide for inviting all types of telegram chat (group and direct) described in "Add new telegram chat help" modal window. 
Next actions available for user: "View/Edit" (redirect user to "Edit telegram group" page), "Remove from folder" (group will be deleted for this folder and alerts of products that are nested in this folder), "Send test message" (test message will be sent in selected group for user request it), "Go to chat" (user will be redirect to telegram group).

![03Folder_Teleggram](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/5cc97993-f74e-4de7-aef7-1c0ff800f686)

Also user could edit telegram chat settings on "Edit telegram group" page that opens by click on the chat name. 
Alert notifications enabled/disabled via "Enable messages" toggle. Parameter "Messages delay" define alert notifications aggregation period.   
List of folders this chat is connected to is presented in Connected folders section.

![03Folder_TeleggrSett](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/35759732-032e-46c4-9867-58c83c4da1e3)

_'Users'_ tab show list of users and their roles. To add new user click "+Add user", select user and set role. It is possible to update a role or remove user from a folder using the operations Edit/Remove in Action column.

![04Folder_Users](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/eebc8b8d-982c-4312-a622-43664cf4753d)
 
### Products

If the user has enough rights, then he can add new products by link "+Add product". A new product will be created and added to the list of products.

![ProductsTabView](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/b5247e88-670e-4dff-978f-e49bbeb53063)

The list of products consists of 4 elements:
1. **Name** - Product name, set when creating a product;
1. **Managers** - Product manager, set when editing a product;
1. **Last Update** - displays the time when the product data was last updated;
1. **Actions** - allow you to edit or delete a product.

To simplify navigation in the list of products, there is a search by (filtering is possible simultaneously by all the following parameters “Name”, “Managers”). 

### Product editing

To open the product edit form user should click on the product name or select "Edit" from the "Actions" context menu.
On this page user could edit product "Name", "Description"; add/edit/remove members and access keys. 

![ProductsEdit](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/64467668-2e85-4dc5-abdc-759959d2ca10)

***
To add a new user to this project, the Admin/Product Manager must select the user's login from the drop-down list, their role (ProductManager and ProductViewer are currently available), and click the "Add" (+) button. After that, the user will appear in the Members list, where his name and role are displayed. Admin/Product Manager can edit the role of this user or remove his access to the project.

![image](https://user-images.githubusercontent.com/43994777/231726900-fd42dd0d-5b30-4673-951b-501c72c803c4.png)
***
### Examples of connecting external services

User could connect external products and HSM using product access key and server address. 
Connection TTS performed via TTA:
![TTS_key](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/df2e4684-1090-44c1-9f0a-41e60c195972)

![TTS_key2](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/7d894a6c-dde3-4976-bdbb-3c63d61a10f3)

Connection aggregator and adminEye performed via adminEye:
![Agg_key](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/01e25846-829e-429a-a7c4-016443818664)

![Agg_key2](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/d7ce063d-d314-4aab-8aa7-f565aaf92f00)

***
**Access keys** - a list of keys that allow the user to send / read data, create nodes / sensors.

The list contains information about:
* **Name** - the name of the key given by the user. When a product is created, a DefaultKey is created;
* **Key** - the key;
* **Copy** - a button that allows you to copy the key to the clipboard;
* **Permissions** - list, permission for the given key. What they can be:
    - CanSendSensorData - the user will be able to send sensor data using this key;
    - CanReadSensorData - the user will be able to read sensor data using this key;
    - CanAddNodes - the user will be able to create nodes for this product;
    - CanAddSensors - the user will be able to create sensors for this product.
    - Full - includes all permissions described above.
* **State** - displays the status of the key. There are 3 statuses possible:
    - Active (![image](https://user-images.githubusercontent.com/43994777/231736874-77570252-28d8-4f78-94d4-ee86649fe723.png)) - this key is available for work, in accordance with its permissions;
    - Expired(![image](https://user-images.githubusercontent.com/43994777/231736905-8284479c-2f90-4998-9735-f77614756fb6.png)) - this key has expired;
    - Blocked(![image](https://user-images.githubusercontent.com/43994777/231736957-9c738ce1-4e73-4455-bae1-9e481db56662.png)) - the key was manually blocked.
* **Actions** - a list of actions that can be performed with a key from the list:
    - Edit - opens the key editing form;
    - Block/Unblock - manually blocks the key, the operations of the corresponding permissions are blocked until the Manager/Admin unblocks the key;
    - Delete - deletes the key.

To add a new key, the user must click "Add key". Then a form for adding a new key appears, which is similar to the edit form, except that it is not possible to change the **Expiration** parameter in the edit form. Below is an example of a form for adding and editing a key.

![NewKew](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/4f98b494-782d-4238-9604-bc38f6d92292)

![EditKey](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/f31e8208-fce3-496f-a821-58058b3b4ba1)

In this form, the user specifies:
* Display name - display name for the key;
* Permissions - all permissions that the key has, the description of each was above;
* Expiration - expiration date of this key (set when creating a key, cannot be edited).

---

## See Also

- [Using HSM Site](Using-HSM-Site) — How to use the web interface
- [Components of HSM Site](Components-of-HSM-Site) — Products, Sensors, and Nodes explained
- [Access Keys](Access-keys) — Detailed access key management
- [Users](Users) — Managing users and their roles
- [Configuration](Configuration) — Admin configuration settings
- [Home](Home) — Main documentation entry point