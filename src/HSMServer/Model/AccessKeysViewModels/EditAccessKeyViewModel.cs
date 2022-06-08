using HSMServer.Attributes;
using HSMServer.Core.Cache.Entities;
using HSMServer.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.AccessKeysViewModels
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


    public class EditAccessKeyViewModel
    {
        public string EncodedProductId { get; set; }

        [Display(Name = "Display name")]
        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(100, ErrorMessage = "{0} length should be less than {1}.")]
        public string DisplayName { get; set; }

        [StringLength(200, ErrorMessage = "{0} length should be less than {1}.")]
        public string Description { get; set; }

        public AccessKeyExpiration Expiration { get; set; }

        public bool CanSendSensorData { get; set; }

        public bool CanAddSensors { get; set; }

        public bool CanAddProducts { get; set; }

        [AccessKeyPermissionsValidation(ErrorMessage = "At least one permission should be selected.")]
        public KeyPermissions Permissions => BuildPermissions();


        // public constructor without parameters for action Home/NewAccessKey
        public EditAccessKeyViewModel() { }


        internal AccessKeyModel ToModel(Guid userId) =>
            new(userId.ToString(), SensorPathHelper.Decode(EncodedProductId))
            {
                DisplayName = DisplayName,
                Comment = Description,
                Permissions = Permissions,
                ExpirationTime = BuildExpirationTime(),
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
                AccessKeyExpiration.Unlimit => DateTime.MaxValue,
                AccessKeyExpiration.Day => expiration.AddDays(1),
                AccessKeyExpiration.Month => expiration.AddMonths(1),
                AccessKeyExpiration.Year => expiration.AddYears(1),
            };
        }
    }
}
