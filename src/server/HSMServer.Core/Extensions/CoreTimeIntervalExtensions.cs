using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;

namespace HSMServer.Core.Extensions;

public static class CoreTimeIntervalExtensions
{
   internal static string ToDisplay(this TimeInterval interval) => interval switch
   {
      TimeInterval.FromFolder => "From Folder",
      TimeInterval.FromParent => "From Parent",
      TimeInterval.ThreeMonths => "Three Months",
      TimeInterval.SixMonths => "Six Months",
      _ => interval.ToString()
   };
   
   public static string GetStringValue(string name, TimeInterval interval)
   {
      if (interval == TimeInterval.None)
      {
         return name switch
         {
            nameof(SettingsCollection.KeepHistory) => TimeInterval.Forever.ToString(),
            nameof(SettingsCollection.SelfDestroy) => TimeInterval.Never.ToString(),
            "Keep sensor history" => TimeInterval.Forever.ToString(),
            "Remove sensor after inactivity" => TimeInterval.Never.ToString(),
            _ => null
         };
      }

      return null;
   }
}