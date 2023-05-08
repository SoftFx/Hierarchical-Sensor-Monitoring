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

    public class AccessKeyCanSendPermission : ValidationAttribute
    { 
        protected override ValidationResult IsValid(object value, ValidationContext context)
        {
            var result = false;
            var cache = (ITreeValuesCache) context.GetService(typeof(ITreeValuesCache));
            
            if (value is Guid id)
            {
                var product = cache?.GetProduct(id);

                if (product != null)
                    result = product.AccessKeys.Values.Any(x => (x.Permissions & KeyPermissions.CanSendSensorData) != 0);
            }
            
            return result ? ValidationResult.Success : new ValidationResult($"There is no access key with {KeyPermissions.CanSendSensorData}");
        }
    }
}