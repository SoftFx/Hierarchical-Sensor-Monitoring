**Product** - a logical group in which a physical computer or a group of computers must be specified. Each product must have a unique name. Each product is assigned a unique key, which is used to send data specific to that particular product. In the client application, the product specifies the root of the tree. Sample product list below:

![1ProductsList](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/269d40d2-0e47-49c3-9b6b-9014ca4f7ec0)

**Sensor** - an entity that transmits its state to the server. The sensor is identified by its name and path in the product tree. An example list of sensors is below:

![2Sensor](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/9ac0ce71-567e-4736-b407-c78d3d6f3418)

**Node** - a subgroup for collecting sensors together that is smaller than a product and is represented by a node in the visual tree. An example node for sensors is below (.module is a node, for sensors Collector version, Service alive, Version
and nodes Collector queue stats, Process dotnet)

![3NodeAndSensors](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/assets/84241056/f91532e4-28cb-4b2a-8a08-6766c036f4cc)
