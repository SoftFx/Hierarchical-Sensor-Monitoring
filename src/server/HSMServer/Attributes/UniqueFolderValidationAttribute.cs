using HSMServer.Folders;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Attributes
{
    public class UniqueFolderValidationAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var folderManager = (IFolderManager)validationContext.GetService(typeof(IFolderManager));

            return value is string name && folderManager[name] == null
                ? ValidationResult.Success
                : new ValidationResult(ErrorMessage);
        }
    }
}
