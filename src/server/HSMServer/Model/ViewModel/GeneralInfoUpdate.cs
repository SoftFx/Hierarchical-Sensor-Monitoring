using System.Collections.Generic;
using HSMServer.Extensions;
using HSMServer.Model.Folders.ViewModels;

namespace HSMServer.Model.ViewModel;

public class GeneralInfoUpdate
{
    public List<KeyValuePair<string, int>> Nodes { get; set; }
    
    public List<KeyValuePair<string, int>> Sensors { get; set; } 
    
    public List<KeyValuePair<string, int>> Products { get; set; }

    public string GrafanaIntegration { get; set; }
    
    
    public GeneralInfoUpdateType UpdateType { get; }

    
    public GeneralInfoUpdate(SensorInfoViewModel sensor)
    {
        GrafanaIntegration = sensor.HasGrafana.ToString();
        UpdateType = GeneralInfoUpdateType.Sensor;
    }
    
    public GeneralInfoUpdate(ProductInfoViewModel product)
    {
        Nodes = product.NodeStatuses.ToStatusCountList();
        Sensors = product.SensorsStatuses.ToStatusCountList();
        UpdateType = GeneralInfoUpdateType.Product;
    }
    
    public GeneralInfoUpdate(FolderInfoViewModel folder)
    {
        Products = folder.ProductStatuses.ToStatusCountList();
        UpdateType = GeneralInfoUpdateType.Folder;
    }
}

public enum GeneralInfoUpdateType
{
    Sensor,
    Product,
    Folder
}