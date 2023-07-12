using HSMCommon.Extensions;
using HSMServer.Extensions;
using HSMServer.Model;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace HSMServer.Attributes;

public class MinTimeIntervalAttribute : ValidationAttribute
{
    private readonly TimeInterval _interval;


    public MinTimeIntervalAttribute(TimeInterval interval)
    {
        _interval = interval;
    }


    public override bool IsValid(object value)
    {
        return value is not TimeIntervalViewModel model || !model.TimeInterval.IsCustom() || model.CustomSpan.Ticks >= (long)_interval;
    }

    public override string FormatErrorMessage(string name) => string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, _interval.GetDisplayName());
}