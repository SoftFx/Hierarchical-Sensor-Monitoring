using System;
using System.ComponentModel.DataAnnotations;
using HSMServer.Attributes;
using HSMServer.Core.Model;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel;

public class EditSensorStatusViewModal
{
    public string ModelHeader => "Edit sensor status";
    
    public string Path { get; set; }
    
    [Display(Name = "Access key")]
    public Guid SelectedAccessKey { get; set; }

    [RequiredKeyPermissions(KeyPermissions.CanSendSensorData)]
    public Guid RootProductId { get; set; }
    
    public string RootProductName { get; set; }
    
    public Guid SensorId { get; set; }
    
    [Display(Name = "Current status")]
    public SensorStatus Status { get; set; }
    
    [Display(Name = "New status")]
    public SensorStatus NewStatus { get; set; }
    
    [Display(Name = "Reason")]
    [Required(ErrorMessage = "Reason required")]
    public string Reason { get; set; }

    
    public EditSensorStatusViewModal() { }

    public EditSensorStatusViewModal(NodeInfoBaseViewModel model)
    {
        Path = model.Header;
        RootProductId = model.RootProductId;
        SensorId = Guid.Parse(model.EncodedId);

        Status = model.Status;
    }
}