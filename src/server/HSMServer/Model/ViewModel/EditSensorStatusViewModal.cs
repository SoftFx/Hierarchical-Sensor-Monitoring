using System;
using System.ComponentModel.DataAnnotations;
using HSMServer.Core.Model;
using HSMServer.Extensions;
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
    
    [Display(Name = "Comment")]
    [Required(ErrorMessage = "Comment required")]
    public string Comment { get; set; }
    
    [Display(Name = "Rewrite Last")]
    public bool RewriteLast { get; set; }
    
    [Display(Name = "New Value")]
    public string NewValue { get; set; }
    
    public bool IsAccessKeyExist { get; internal set; }


    public bool IsValueChangeBlockDisplayed { get; private set; } = true;
    
    public EditSensorStatusViewModal() { }

    public EditSensorStatusViewModal(SensorNodeViewModel model, bool isAccessKeyExist = false)
    {
        Path = model.FullPath;
        RootProductId = model.RootProduct.Id;
        SensorId = model.EncodedId.ToGuid();
        IsAccessKeyExist = isAccessKeyExist;

        Status = model.Status;

        IsValueChangeBlockDisplayed = model.Type is not (SensorType.File or SensorType.DoubleBar or SensorType.IntegerBar);
    }
}