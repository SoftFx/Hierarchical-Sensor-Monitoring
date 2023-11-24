using HSMServer.Core.Model;

namespace HSMServer.DTOs.SensorInfo;

public record SensorInfoDto(SensorType realType, SensorType plotType, string units);