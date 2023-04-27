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
            model.SelectedProductId ??= Guid.Empty.ToString();

            if (model.Id != Guid.Empty)
            {
                var key = cache.GetAccessKey(model.Id);
                
                if (key.ProductId == Guid.Empty)
                {
                    var serverKeys = cache.GetAccessKeys().Where(x => x.ProductId == Guid.Empty);
                    return !serverKeys.Any(x => x.DisplayName == model.DisplayName && x.Id != model.Id);
                }
                
                var keys = cache.GetProduct(key.ProductId).AccessKeys;
                
                return !keys.Values.Any(x => x.DisplayName == model.DisplayName && x.Id != model.Id);
            }
            
            if (Guid.TryParse(model.SelectedProductId, out var id) && id == Guid.Empty)
            {
                var serverKeys = cache.GetAccessKeys().Where(x => x.ProductId == Guid.Empty);

                return !serverKeys.Any(x => x.DisplayName == model.DisplayName && x.Id != model.Id);
            }
            
            var product = cache.GetProduct(id);

            return !product.AccessKeys.Values.Any(x => x.DisplayName == model.DisplayName && x.Id != model.Id);
        }
    }
}
