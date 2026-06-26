**Product** - a logical group, which must specify a physical computer or a group of computers. Every product must have a unique name. Every product is given a unique key, which is used to send data, corresponding to that particular product. In the client app, the product specifies the tree root.  

**Sensor** - an entity, which gives its' states to the server. The sensor is defined by name and path in the product tree.  

**Product Key** - a characters string, which is given to a product during the process of adding. Product key used to pass sensor data and distinguish the product.

**Node** - a subgroup for collecting sensors together, which is smaller than product and is presented as the node in visual tree.

**Admin** - the role of a user, which means that the user has all possible access. Admin can view data from all existing products, manage all the products, and may access the Admin page, where configuration parameters can be edited.

**Product Manager** - the role, which exists inside the product. Basically grants access to edit product page, where the manager can add and/or invite new viewers and managers via email, and add new extra product keys.

**Product Viewer** - the simplest role, which allows the user to see the product in the monitoring tree and on products page, but does not give access to edit product page. 