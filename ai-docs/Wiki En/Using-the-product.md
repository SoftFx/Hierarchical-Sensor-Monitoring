HSM server is made to collect data from various sources, https API is the currently supported one. 

## Products

After you have configured and launched server and client apps, you need to add at least one product to get started. To add a new product, use the Products window from the client app. You product will be given the unique key, which you will need to send sensors data for this product.

## API endpoints

You may use the web api, which is running on the port 44330. There are different ways:
1. Use some advanced software for testing APIs, such as [Postman](https://www.postman.com/). You need to send POST requests and add a header with Content-Type = "application/json".
2. Use any programming language you wish and send the request with the headers from above and a data template from the page [Sensors](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Sensors).
3. You might use the swagger UI, which is a simple and fast way for sending data. Swagger also provides some examples of the data. Swagger UI is running on the address {url}:44330/api/swagger/index.html, where 'url' is the address of your HSM server (the one you specify as an 'address' in [Client configuration](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/wiki/Configuration#client-configuration)).
