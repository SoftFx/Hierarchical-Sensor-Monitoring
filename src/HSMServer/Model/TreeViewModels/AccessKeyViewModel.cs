using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.TreeViewModels
{
    public enum AccessKeyExpiration
    {
        [Display(Name = "Unlimit")]
        Unlimit,
        [Display(Name = "1 Day")]
        Day,
        [Display(Name = "1 Month")]
        Month,
        [Display(Name = "1 Year")]
        Year,
    }


    public class AccessKeyViewModel
    {
        public Guid Id { get; }

        public DateTime ExpirationDate { get; }

        public string Permissions { get; }

        public string EncodedProductId { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public AccessKeyExpiration Expiration { get; set; }

        public bool CanSendData { get; set; }

        public bool CanAddSensors { get; set; }

        public bool CanAddProducts { get; set; }


        // public constructor without parameters for action Home/NewAccessKey
        public AccessKeyViewModel() { }

        internal AccessKeyViewModel(AccessKeyModel accessKey)
        {
            Id = accessKey.Id;
            DisplayName = accessKey.DisplayName;
            Description = accessKey.Comment;
            ExpirationDate = accessKey.ExpirationTime;
            Permissions = BuildPermissions(accessKey.Permissions);
        }


        internal AccessKeyModel ToModel(Guid userId) =>
            new(userId.ToString(), SensorPathHelper.Decode(EncodedProductId))
            {
                DisplayName = DisplayName,
                Comment = Description,
                Permissions = BuildPermissions(),
                ExpirationTime = BuildExpirationTime(),
            };

        private KeyPermissions BuildPermissions()
        {
            var perm = KeyPermissions.CanSendSensorData |
                       KeyPermissions.CanAddProducts |
                       KeyPermissions.CanAddSensors;

            if (!CanSendData)
                perm &= ~KeyPermissions.CanSendSensorData;
            if (!CanAddProducts)
                perm &= ~KeyPermissions.CanAddProducts;
            if (!CanAddSensors)
                perm &= ~KeyPermissions.CanAddSensors;

            return perm;
        }

        private DateTime BuildExpirationTime()
        {
            var expiration = DateTime.UtcNow;

            return Expiration switch
            {
                AccessKeyExpiration.Unlimit => DateTime.MaxValue,
                AccessKeyExpiration.Day => expiration.AddDays(1),
                AccessKeyExpiration.Month => expiration.AddMonths(1),
                AccessKeyExpiration.Year => expiration.AddYears(1),
            };
        }

        private static string BuildPermissions(KeyPermissions permissions)
        {
            var result = new List<string>(2);

            if (permissions.HasFlag(KeyPermissions.CanSendSensorData))
                result.Add("Send data");
            if (permissions.HasFlag(KeyPermissions.CanAddProducts))
                result.Add("Add product(s)");
            if (permissions.HasFlag(KeyPermissions.CanAddSensors))
                result.Add("Add sensor(s)");

            return string.Join(", ", result);
        }
    }
}
