using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "1 hour")]
        Hour,
        [Display(Name = "1 day")]
        Day,
        [Display(Name = "1 week")]
        Week,
        [Display(Name = "1 month")]
        Month,
        Custom,
    }


    public class TimeIntervalViewModel
    {
        public TimeInterval TimeInterval { get; set; }

        public string CustomTimeInterval { get; set; }


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }
    }
}
