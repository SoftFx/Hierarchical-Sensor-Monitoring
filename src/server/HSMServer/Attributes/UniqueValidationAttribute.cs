using System;
using System.Collections.Generic;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.ViewModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HSMServer.Core.Model;
using HSMServer.Model.AccessKeysViewModels;

namespace HSMServer.Attributes
{
    public class UniqueValidationAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext context)
        {
            var result = false;

            if (value is string name)
            {
                var folderManager = (IFolderManager)context.GetService(typeof(IFolderManager));
                var cache = (ITreeValuesCache)context.GetService(typeof(ITreeValuesCache));

                result = context.ObjectInstance switch
                {
                    EditFolderViewModel folder => !folder.IsNameChanged || folderManager[name] == null,
                    AddProductViewModel => cache.GetProductByName(name) == null,
                    EditAccessKeyViewModel model => AccessKeyNameCheck(model, cache),
                    _ => false
                };
            }

            return result ? ValidationResult.Success : new ValidationResult(ErrorMessage);
        }

        private static bool AccessKeyNameCheck(EditAccessKeyViewModel model, ITreeValuesCache cache)
        {
            IEnumerable<AccessKeyModel> keys;
            
            if (model.Id != Guid.Empty)
            {
                var key = cache.GetAccessKey(model.Id);
                
                keys = key.IsMaster ? cache.GetMasterKeys() : GetProductKeys(cache, key.ProductId);
            }
            else
                keys = model.IsMaster ? cache.GetMasterKeys() : GetProductKeys(cache, model.SelectedProductId);

            return IsValidAccessKey(keys, model.DisplayName, model.Id);
        }

        private static bool IsValidAccessKey(IEnumerable<AccessKeyModel> keys, string displayName, Guid id) => 
            !keys.Any(x => x.DisplayName == displayName && x.Id != id);

        private static IEnumerable<AccessKeyModel> GetProductKeys(ITreeValuesCache cache, Guid productId) =>
            cache.GetProduct(productId).AccessKeys.Values;
    }
}
