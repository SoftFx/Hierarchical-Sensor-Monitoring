using System.Linq;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.UserFilters
{
    public abstract class UserFilterGroupBase
    {
        [JsonIgnore]
        internal abstract FilterProperty[] Properties { get; }

        [JsonIgnore]
        internal abstract FilterGroupType Type { get; }


        [JsonIgnore]
        public int EnableFiltersCount => Properties.Count(p => p.Value);

        [JsonIgnore]
        public bool HasAnyEnabledFilters => Properties.Any(p => p.Value);


        internal abstract bool IsSensorSuitable(FilteredSensor sensor);

        internal virtual bool NeedToCheckSensor(FilterGroupType mask) =>
            mask.HasFlag(Type);
    }


    public sealed class FilterProperty
    {
        public bool Value { get; set; }
        
        [JsonIgnore]
        public string Name { get; }

        public FilterProperty() { }

        public FilterProperty(string name)
        {
            Name = name;
        }
        
    }
}
