using System;
using System.ComponentModel.DataAnnotations;
using HSMServer.Model.TreeViewModel;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel;

public class EditSensorStatusViewModal
{
    public const string AccessKeyValidationErrorMessage = "There is no suitable access key for this operation";
    
    
    public string Path { get; set; }

    public Guid RootProductId { get; set; }

    public Guid SensorId { get; set; }
    
    [Display(Name = "Current status")]
    public SensorStatus Status { get; set; }
    
    [Display(Name = "New status")]
    public SensorStatus NewStatus { get; set; }
    
    [Display(Name = "Reason")]
    [Required(ErrorMessage = "Reason required")]
    public string Reason { get; set; }
    
    public bool IsAccessKeyExist { get; internal set; }
    
    
    public EditSensorStatusViewModal() { }

    public EditSensorStatusViewModal(SensorNodeViewModel model, bool isAccessKeyExist = false)
    {
        Path = model.FullPath;
        RootProductId = model.RootProduct.Id;
        SensorId = Guid.Parse(model.EncodedId);
        IsAccessKeyExist = isAccessKeyExist;

        Status = model.Status;
    }
}