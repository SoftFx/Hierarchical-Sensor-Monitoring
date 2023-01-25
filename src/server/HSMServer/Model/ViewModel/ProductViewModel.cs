using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Constants;
using HSMServer.Authentication;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public Guid Id { get; }

        public string EncodedId { get; }

        public string Key { get; }

        public string Name { get; }

        public bool ProductUpdateIsExpired { get; }

        public string ProductExpiredMessage { get; }

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
            Managers = userManager.GetManagers(Id).Select(manager => manager.UserName).ToList();
            ProductUpdateIsExpired = (DateTime.UtcNow - LastUpdateDate).Ticks >= ConfigurationConstants.DefaultExpirationTime.Ticks;
            ProductExpiredMessage = $"Sensor hasn't been updated since {(DateTime.UtcNow - ConfigurationConstants.DefaultExpirationTime).ToDefaultFormat()}";
        }
    }
}