﻿using HSMServer.Core.Cache.Entities;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Attributes
{
    public class AccessKeyPermissionsValidationAttribute : ValidationAttribute
    {
        public override bool IsValid(object value) =>
            value is KeyPermissions permissions && permissions != 0;
    }
}
