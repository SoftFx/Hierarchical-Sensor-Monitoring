using System;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.ViewModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
                    EditFolderViewModel => folderManager[name] == null,
                    AddProductViewModel => cache.GetProductByName(name) == null,
                    EditAccessKeyViewModel model => AccessKeyNameCheck(model, cache),
                    _ => false
                };
            }

            return result ? ValidationResult.Success : new ValidationResult(ErrorMessage);
        }

        private bool AccessKeyNameCheck(EditAccessKeyViewModel model, ITreeValuesCache cache)
        {
            if (string.IsNullOrEmpty(model.EncodedProductId) && string.IsNullOrEmpty(model.SelectedProduct))
            {
                return !cache.GetAccessKeys().Any(x => x.DisplayName.Equals(model.DisplayName, StringComparison.InvariantCultureIgnoreCase));
            }
            
            model.EncodedProductId ??= model.SelectedProduct;
            model.SelectedProduct ??= model.EncodedProductId;
            
            var product = cache.GetProduct(Guid.Parse(model.SelectedProduct));

            return !product.AccessKeys.Values.Any(x => x.DisplayName.Equals(model.DisplayName, StringComparison.InvariantCultureIgnoreCase));
        }
        
    }
}
