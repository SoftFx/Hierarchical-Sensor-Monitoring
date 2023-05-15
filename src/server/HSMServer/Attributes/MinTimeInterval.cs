using System.ComponentModel.DataAnnotations;
using System.Globalization;
using HSMServer.Extensions;
using HSMServer.Model;

namespace HSMServer.Attributes;

public class MinTimeInterval : ValidationAttribute
{
    private readonly TimeInterval _interval;
    
    
    public MinTimeInterval(TimeInterval interval)
    {
        _interval = interval;
    }

    public override bool IsValid(object value)
    {
        if (value is TimeIntervalViewModel model && model.TimeInterval.IsCustom())
            return model.TimeInterval.ToCustomTicks(model.CustomTimeInterval) > _interval.ToCustomTicks(string.Empty);

        return true;
    }
    
    public override string FormatErrorMessage(string name) => string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, _interval.GetDisplayName());
}