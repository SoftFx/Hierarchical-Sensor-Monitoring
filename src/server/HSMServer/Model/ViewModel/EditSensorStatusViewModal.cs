using System;
using System.ComponentModel.DataAnnotations;
using HSMServer.Core.Model;
using HSMServer.Extensions;

namespace HSMServer.Model.ViewModel;

public class EditSensorStatusViewModal
{
    public readonly string ModelHeader = "Edit sensor status";
    
    public string Path { get; set; }
    
    [Display(Name = "Access key")]
    public Guid SelectedAccessKey { get; set; }
    
    public Guid RootProductId { get; set; }
    
    public Guid SensorId { get; set; }
    
    [Display(Name = "Current Status")]
    public SensorStatus Status { get; set; }
    
    [Display(Name = "New Status")]
    public SensorStatus NewStatus { get; set; }
    
    [Required(ErrorMessage = "Reason required")]
    [Display(Name = "Reason")]
    public string Reason { get; set; }

    public EditSensorStatusViewModal() { }

    public EditSensorStatusViewModal(SensorInfoViewModel sensorInfoViewModel)
    {
        Path = sensorInfoViewModel.Path;
        RootProductId = sensorInfoViewModel.RootProductId;
        SensorId = Guid.Parse(sensorInfoViewModel.EncodedId);

        Status = sensorInfoViewModel.Status.ToCore();
    }
}