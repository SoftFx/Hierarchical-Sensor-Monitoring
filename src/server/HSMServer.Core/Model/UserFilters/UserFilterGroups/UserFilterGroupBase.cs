using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.UserFilter
{
    public abstract class UserFilterGroupBase
    {
        private readonly List<FilterProperty> _properties = new();

        [JsonIgnore]
        public abstract FilterGroupType Type { get; }


        [JsonIgnore]
        public int EnableFiltersCount => _properties.Count(p => p.Value);

        [JsonIgnore]
        public bool HasAnyEnabledFilters => _properties.Any(p => p.Value);


        internal abstract void RegisterProperties();

        protected void RegisterProperty(FilterProperty property) =>
            _properties.Add(property);
    }


    public sealed class FilterProperty
    {
        public bool Value { get; init; }


        public FilterProperty() { }
    }
}
