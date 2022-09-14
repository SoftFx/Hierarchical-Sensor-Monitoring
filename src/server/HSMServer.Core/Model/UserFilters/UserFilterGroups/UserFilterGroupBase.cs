using System.Linq;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.UserFilter
{
    public abstract class UserFilterGroupBase
    {
        [JsonIgnore]
        protected abstract FilterProperty[] Properties { get; }

        [JsonIgnore]
        internal abstract FilterGroupType Type { get; }


        [JsonIgnore]
        public int EnableFiltersCount => Properties.Count(p => p.Value);

        [JsonIgnore]
        public bool HasAnyEnabledFilters => Properties.Any(p => p.Value);
    }


    public sealed class FilterProperty
    {
        public bool Value { get; init; }


        public FilterProperty() { }
    }
}
