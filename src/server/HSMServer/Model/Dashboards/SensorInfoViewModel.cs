using HSMServer.Core.Model;

namespace HSMServer.Model.Dashboards;

public record SensorInfoViewModel(SensorType realType, SensorType plotType, string units);