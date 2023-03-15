using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Constants;
using HSMServer.Authentication;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public static TimeSpan ProductExpiredTime => ConfigurationConstants.DefaultExpirationTime;
        
        
        public Guid Id { get; }

        public string EncodedId { get; }

        public string Key { get; }

        public string Name { get; }

        public bool ProductUpdateIsExpired { get; }

        public string ShortLastUpdateTime { get; }

        public DateTime LastUpdateDate { get; }

        public List<string> Managers { get; }

        public ProductViewModel(ProductNodeViewModel product, IUserManager userManager)
        {
            Id = product.Id;
            EncodedId = SensorPathHelper.EncodeGuid(product.Id);
            Key = product.AccessKeys.FirstOrDefault().Value?.Id.ToString();
            Name = product.Name;
            LastUpdateDate = product.UpdateTime;
            ShortLastUpdateTime = LastUpdateDate.GetTimeAgo();
            Managers = userManager.GetManagers(Id).Select(manager => manager.Name).ToList();
            ProductUpdateIsExpired = (DateTime.UtcNow - LastUpdateDate).Ticks >= ProductExpiredTime.Ticks;
        }
    }
}