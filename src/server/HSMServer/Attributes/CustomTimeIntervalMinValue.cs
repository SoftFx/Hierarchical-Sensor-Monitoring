using System.ComponentModel.DataAnnotations;
using HSMServer.Extensions;
using HSMServer.Model;

namespace HSMServer.Attributes;

public class CustomTimeIntervalMinValue : ValidationAttribute
{
    private readonly long _ticks;
    
    public CustomTimeIntervalMinValue(long ticks)
    {
        _ticks = ticks;
    }

    protected override ValidationResult IsValid(object value, ValidationContext context)
    {
        if (value is TimeIntervalViewModel model && model.TimeInterval.IsCustom())
            return model.TimeInterval.ToCustomTicks(model.CustomTimeInterval) > _ticks ? ValidationResult.Success : new ValidationResult(ErrorMessage);

        return ValidationResult.Success;
    }
}