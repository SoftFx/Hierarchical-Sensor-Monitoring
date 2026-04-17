# Using the Product

HSM server is made to collect data from various sources. The currently supported API is HTTPS.

See also: [REST API](REST-API), [HSM DataCollector](HSMDataCollector), [Core Concepts](Core-Concepts)

---

## Products

After you have configured and launched the server and client apps, you need to add at least one product to get started. To add a new product, use the Products window from the client app. Your product will be given a unique [Access Key](Access-keys), which you will need to send sensor data for this product.

See also: [Site Structure → Products](Site-structure#products), [Core Concepts → Product](Core-Concepts#product)

## API Endpoints

You may use the web API, which is running on port `44330`. There are different ways to interact:

1. **API Testing Tools** — Use software such as [Postman](https://www.postman.com/). You need to send POST requests and add a header with `Content-Type = "application/json"`.
2. **Programming Languages** — Use any programming language and send requests with the headers from above and a data template from the [REST API](REST-API) page.
3. **Swagger UI** — A simple and fast way for sending data. Swagger also provides some examples of the data. Swagger UI is running on the address `{url}:44330/api/swagger/index.html`, where `url` is the address of your HSM server.

---

## See Also

- [REST API](REST-API) — Full REST API reference
- [HSM DataCollector](HSMDataCollector) — NuGet package for .NET applications
- [Installation](Installation) — How to deploy the HSM server
- [Getting Started](Getting-Started) — How HSM works and key concepts
