using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.ViewModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Attributes
{
    public class UniqueValidationAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var result = false;

            if (value is string name)
            {
                if (validationContext.ObjectInstance is EditFolderViewModel)
                {
                    var folderManager = (IFolderManager)validationContext.GetService(typeof(IFolderManager));

                    result = folderManager[name] == null;
                }

                if (validationContext.ObjectInstance is AddProductViewModel)
                {
                    var cache = (ITreeValuesCache)validationContext.GetService(typeof(ITreeValuesCache));

                    result = !cache.GetProducts().Any(p => p.DisplayName == name);
                }
            }

            return result ? ValidationResult.Success : new ValidationResult(ErrorMessage);
        }
    }
}
