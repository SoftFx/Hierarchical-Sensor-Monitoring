using HSMServer.Core.Attributes;
using System.ComponentModel.DataAnnotations;


namespace HSMServer.Core.Model
{
    public enum Unit : int
    {
        [Group(1, "Data size", 1)]
        bits = 0,
        [Group(1, "Data size", 2)]
        bytes = 1,
        [Group(1, "Data size", 3)]
        KB = 2,
        [Group(1, "Data size", 4)]
        MB = 3,
        [Group(1, "Data size", 5)]
        GB = 4,

        [Display(Name = "%")]
        Percents = 100,

        [Group(2, "Time interval", 1)]
        [Display(Name = "ticks")]
        Ticks = 1000,
        [Group(2, "Time interval", 2)]
        [Display(Name = "ms")]
        Milliseconds = 1010,
        [Group(2, "Time interval", 3)]
        [Display(Name = "sec")]
        Seconds = 1011,
        [Group(2, "Time interval", 4)]
        [Display(Name = "min")]
        Minutes = 1012,

        [Display(Name = "count")]
        Count = 1100,
        [Display(Name = "requests")]
        Requests = 1101,
        [Display(Name = "responses")]
        Responses = 1102,

        [Group(3, "Data rate", 1)]
        [Display(Name = "bits/sec")]
        Bits_sec = 2100,
        [Group(3, "Data rate", 2)]
        [Display(Name = "Bytes/sec")]
        Bytes_sec = 2101,
        [Group(3, "Data rate", 3)]
        [Display(Name = "KB/sec")]
        KBytes_sec = 2102,
        [Group(3, "Data rate", 4)]
        [Display(Name = "MB/sec")]
        MBytes_sec = 2103,

        [Display(Name = "# per sec")]
        ValueInSecond = 3000,
    }
}
