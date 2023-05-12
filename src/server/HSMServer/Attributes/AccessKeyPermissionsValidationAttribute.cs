using System;
using HSMServer.Core.Model;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HSMServer.Core.Cache;

namespace HSMServer.Attributes
{
    public class AccessKeyPermissionsValidationAttribute : ValidationAttribute
    {
        public override bool IsValid(object value) =>
            value is KeyPermissions permissions && permissions != 0;
    }

    public class RequiredKeyPermissions : ValidationAttribute
    {
        private readonly ValidationResult _validationError;
        
        private readonly KeyPermissions _permissions;
        
        public RequiredKeyPermissions(KeyPermissions permissions)
        {
            _permissions = permissions;
            _validationError = new ValidationResult($"There is no access key with {_permissions}");
        }
        
        protected override ValidationResult IsValid(object value, ValidationContext context)
        {
            var result = false;
            
            if (value is Guid id)
            {
                var cache = (ITreeValuesCache) context.GetService(typeof(ITreeValuesCache));
                var product = cache?.GetProduct(id);

                if (product != null)
                    result = product.AccessKeys.Values.Any(x => x.Permissions.HasFlag(_permissions));
            }

            return result ? ValidationResult.Success : _validationError;
        }
    }
}