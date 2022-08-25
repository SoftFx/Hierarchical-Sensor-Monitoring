using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public enum TimeRange
    {
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "1 hour")]
        Hour,
        [Display(Name = "1 day")]
        Day,
        [Display(Name = "1 month")]
        Month,
        [Display(Name = "1 week")]
        Week,
        [Display(Name = "1 Year")]
        Year,
        Custom,
    }


    public class TimeRangeViewModel
    {
        public TimeRange TimeRange { get; set; }

        public string CustomTimeSpan { get; set; }


        // public constructor without parameters for post actions
        public TimeRangeViewModel() { }
    }
}
