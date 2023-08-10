using HSMServer.Core.Model;

namespace HSMServer.Core.Extensions;

internal static class CoreTimeIntervalExtensions
{
   internal static string ToDisplay(this TimeInterval interval) => interval switch
   {
      TimeInterval.FromFolder => "From Folder",
      TimeInterval.FromParent => "From Parent",
      TimeInterval.ThreeMonths => "Three Months",
      TimeInterval.SixMonths => "Six Months",
      _ => interval.ToString()
   };
}