using HSMServer.Core.Model;
using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public Guid Id { get; }

        public string EncodedId { get; }

        public string Key { get; }

        public string Name { get; }

        public string ShortLastUpdateTime { get; }

        public DateTime CreationDate { get; }

        public DateTime LastUpdateDate { get; }

        public List<string> Managers { get; }

        public ProductViewModel(List<string> managers, ProductModel product)
        {
            Id = product.Id;
            EncodedId = SensorPathHelper.EncodeGuid(product.Id);
            Key = product.AccessKeys.FirstOrDefault().Value?.Id.ToString();
            Name = product.DisplayName;
            CreationDate = product.CreationDate;
            LastUpdateDate = product.LastUpdateDate == DateTime.MinValue ? CreationDate : product.LastUpdateDate;
            ShortLastUpdateTime = GetTimeAgo(this);
            Managers = managers;
        }

        private static string GetTimeAgo(ProductViewModel productViewModel)
        {
            string UnitsToString(double value, string unit)
            {
                int intValue = Convert.ToInt32(value);
                return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
            }

            var time = new TimeSpan((DateTime.UtcNow - productViewModel.LastUpdateDate).Ticks);

            if (time.TotalDays > 30)
                return "> a month ago";
            else if (time.TotalDays >= 1)
                return $"> {UnitsToString(time.TotalDays, "day")} ago";
            else if (time.TotalHours >= 1)
                return $"> {UnitsToString(time.TotalHours, "hour")} ago";
            else if (time.TotalMinutes >= 1)
                return $"{UnitsToString(time.TotalMinutes, "minute")} ago";
            else if (time.TotalSeconds < 60)
                return "< 1 minute ago";

            return "no info";
        }
    }
}