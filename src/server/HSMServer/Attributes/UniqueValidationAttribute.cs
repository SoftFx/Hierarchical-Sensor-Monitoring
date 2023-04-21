using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.ViewModel;
using System.ComponentModel.DataAnnotations;

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
                    _ => false
                };
            }

            return result ? ValidationResult.Success : new ValidationResult(ErrorMessage);
        }
    }
}
