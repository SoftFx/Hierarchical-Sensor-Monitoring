Simple sensors:
* Bool - [possible values](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/bool)
* Integer - [possible values](https://learn.microsoft.com/en-us/dotnet/api/system.int32?view=net-7.0)
* Double - [possible values](https://learn.microsoft.com/en-us/dotnet/api/system.double?view=net-7.0)
* String - [possible values](https://learn.microsoft.com/en-us/dotnet/api/system.string?view=net-7.0)
* Timespan [(v3.15.0)](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/releases/tag/server-v3.15.0) - [possible values](https://learn.microsoft.com/en-us/dotnet/api/system.timespan?view=net-7.0)
* Version [(v3.18.0)](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/releases/tag/server-v3.18.0) - [possible values](https://learn.microsoft.com/en-us/dotnet/api/system.version?view=net-7.0)

Bar sensors:
* IntegerBar - a bar that contains [int](https://learn.microsoft.com/en-us/dotnet/api/system.int32?view=net-7.0) Min, Max, Mean, Last Value properties for some period of time
* DoubleBar - a bar that contains [double](https://learn.microsoft.com/en-us/dotnet/api/system.double?view=net-7.0) Min, Max, Mean, Last Value properties for some period of time

Advanced sensors:
* File - any file that can be converted to a byte stream
