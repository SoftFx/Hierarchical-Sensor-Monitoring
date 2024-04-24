using HSMServer.Authentication;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ViewModel
{
    public record ProductViewModel
    {
        public static readonly TimeSpan ProductExpiredTime = new(30, 0, 0, 0); // TODO : get DefaultExpirationTime from IServerConfig


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