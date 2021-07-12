using FluentValidation.Results;
using System.Collections.Generic;
using System.Text;


namespace HSMServer.Model.Validators
{
    public static class ValidatorHelper
    {
        public static string GetErrorString(List<ValidationFailure> errors)
        {
            StringBuilder result = new StringBuilder();

            foreach (var error in errors)
            {
                result.Append(error.ErrorMessage.Replace('\'', ' ')); 
                return result.ToString();
            }

            return result.ToString();
        }
    }
}
