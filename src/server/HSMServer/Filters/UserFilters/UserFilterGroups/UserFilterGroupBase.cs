using System.Linq;
using System.Text.Json.Serialization;

namespace HSMServer.UserFilters
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
    }


    public sealed class FilterProperty
    {
        [JsonIgnore]
        public string Name { get; set; }

        public bool Value { get; set; }


        public FilterProperty() { }

        public FilterProperty(string name)
        {
            Name = name;
        }

        public FilterProperty(string name, bool defaultValue) : this(name)
        {
            Value = defaultValue;
        }
    }
}
