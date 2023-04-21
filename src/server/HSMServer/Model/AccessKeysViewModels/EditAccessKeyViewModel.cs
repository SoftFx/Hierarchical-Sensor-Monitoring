using HSMServer.Attributes;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using HSMServer.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;

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

        public string ExpirationTime { get; init; }

        public bool CloseModal { get; set; }

        public bool IsModify { get; set; }


        public string EncodedProductId { get; set; }

        [Display(Name = "Display name")]
        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(100, ErrorMessage = "{0} length should be less than {1}.")]
        [UniqueValidation(ErrorMessage = "Access key with the same name already exists.")]
        public string DisplayName { get; set; }

        public AccessKeyExpiration Expiration { get; set; }

        public bool CanSendSensorData { get; set; }

        public bool CanAddNodes { get; set; }

        public bool CanAddSensors { get; set; }

        public bool CanReadSensorData { get; set; }

        public bool CanUseGrafana { get; set; }

        [AccessKeyPermissionsValidation(ErrorMessage = "At least one permission should be selected.")]
        public KeyPermissions Permissions => BuildPermissions();
        

        public List<ProductModel> Products { get; set; } = new ();

        public List<SelectListItem> ProductsItems => Products.Select(x => new SelectListItem()
        {
            Text = x.DisplayName,
            Value = x.Id.ToString(),
            Selected = x.DisplayName == SelectedProduct
        }).ToList();
        

        [Display(Name = "Product name")]
        public string SelectedProduct { get; set; }


        // public constructor without parameters for action Home/NewAccessKey
        public EditAccessKeyViewModel()
        {
        }

        public EditAccessKeyViewModel(AccessKeyModel key)
        {
            Id = key.Id;

            DisplayName = key.DisplayName;
            ExpirationTime = AccessKeyViewModel.BuildExpiration(key.ExpirationTime);

            CanSendSensorData = key.Permissions.HasFlag(KeyPermissions.CanSendSensorData);
            CanAddNodes = key.Permissions.HasFlag(KeyPermissions.CanAddNodes);
            CanAddSensors = key.Permissions.HasFlag(KeyPermissions.CanAddSensors);
            CanReadSensorData = key.Permissions.HasFlag(KeyPermissions.CanReadSensorData);
            CanUseGrafana = key.Permissions.HasFlag(KeyPermissions.CanUseGrafana);
        }

        public EditAccessKeyViewModel GenerateProducts(List<ProductModel> products)
        {
            CloseModal = true;
            Products = products;
            Guid.TryParse(EncodedProductId, out var id);
            SelectedProduct = Products.FirstOrDefault(x => x.Id == id)?.DisplayName;
            return this;
        }


        internal AccessKeyModel ToModel(Guid userId)
        {
            AccessKeyModel accessKey = new(userId, string.IsNullOrEmpty(EncodedProductId) ? Guid.Empty : SensorPathHelper.DecodeGuid(EncodedProductId))
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
                Permissions = Permissions,
            };

        private KeyPermissions BuildPermissions()
        {
            KeyPermissions perm = 0;

            if (CanSendSensorData)
                perm |= KeyPermissions.CanSendSensorData;
            if (CanAddNodes)
                perm |= KeyPermissions.CanAddNodes;
            if (CanAddSensors)
                perm |= KeyPermissions.CanAddSensors;
            if (CanReadSensorData)
                perm |= KeyPermissions.CanReadSensorData;
            if (CanUseGrafana)
                perm |= KeyPermissions.CanUseGrafana;

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