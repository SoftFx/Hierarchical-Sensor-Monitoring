using HSMServer.Core.Model;

namespace HSMServer.DTOs.SensorInfo;

public record SensorInfoDTO(SensorType realType, SensorType plotType, string units);