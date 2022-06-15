using HSMServer.Attributes;
using HSMServer.Core.Cache.Entities;
using HSMServer.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.AccessKeysViewModels
{
    public enum AccessKeyExpiration
    {
        [Display(Name = "Unlimited")]
        Unlimited,
        [Display(Name = "1 Day")]
        Day,
        [Display(Name = "1 Month")]
        Month,
        [Display(Name = "1 Year")]
        Year,
    }


    public class EditAccessKeyViewModel
    {
        public Guid Id { get; set; }

        public string ExpirationTime { get; }

        public bool CloseModal { get; init; }

        public bool IsModify { get; init; }


        public string EncodedProductId { get; set; }

        [Display(Name = "Display name")]
        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(100, ErrorMessage = "{0} length should be less than {1}.")]
        public string DisplayName { get; set; }

        [StringLength(200, ErrorMessage = "{0} length should be less than {1}.")]
        public string Description { get; set; }

        public AccessKeyExpiration Expiration { get; set; }

        public bool CanSendSensorData { get; set; }

        public bool CanAddProducts { get; set; }

        public bool CanAddSensors { get; set; }

        [AccessKeyPermissionsValidation(ErrorMessage = "At least one permission should be selected.")]
        public KeyPermissions Permissions => BuildPermissions();


        // public constructor without parameters for action Home/NewAccessKey
        public EditAccessKeyViewModel() { }

        public EditAccessKeyViewModel(AccessKeyModel key)
        {
            Id = key.Id;

            DisplayName = key.DisplayName;
            Description = key.Comment;
            ExpirationTime = AccessKeyViewModel.BuildExpiration(key.ExpirationTime);

            CanSendSensorData = key.KeyPermissions.HasFlag(KeyPermissions.CanSendSensorData);
            CanAddProducts = key.KeyPermissions.HasFlag(KeyPermissions.CanAddProducts);
            CanAddSensors = key.KeyPermissions.HasFlag(KeyPermissions.CanAddSensors);
        }


        internal AccessKeyModel ToModel(Guid userId)
        {
            AccessKeyModel accessKey = new(userId.ToString(), SensorPathHelper.Decode(EncodedProductId))
            {
                ExpirationTime = BuildExpirationTime(),
            };

            return accessKey.Update(ToAccessKeyUpdate());
        }

        internal AccessKeyUpdate ToAccessKeyUpdate() =>
             new()
             {
                 Id = Id,
                 DisplayName = DisplayName,
                 Comment = Description ?? string.Empty,
                 Permissions = Permissions,
             };

        private KeyPermissions BuildPermissions()
        {
            KeyPermissions perm = 0;

            if (CanSendSensorData)
                perm |= KeyPermissions.CanSendSensorData;
            if (CanAddProducts)
                perm |= KeyPermissions.CanAddProducts;
            if (CanAddSensors)
                perm |= KeyPermissions.CanAddSensors;

            return perm;
        }

        private DateTime BuildExpirationTime()
        {
            var expiration = DateTime.UtcNow;

            return Expiration switch
            {
                AccessKeyExpiration.Unlimited => DateTime.MaxValue,
                AccessKeyExpiration.Day => expiration.AddDays(1),
                AccessKeyExpiration.Month => expiration.AddMonths(1),
                AccessKeyExpiration.Year => expiration.AddYears(1),
            };
        }
    }
}
