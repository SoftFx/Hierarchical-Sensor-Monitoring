using System;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using HSMServer.Constants;
using HSMServer.Core.Cache;

namespace HSMServer.Model.Validators;

public class AccessKeyNameValidator : AbstractValidator<string>
{
    private readonly ITreeValuesCache _cache;


    public AccessKeyNameValidator(ITreeValuesCache cache)
    {
        _cache = cache;

        RuleFor(x => x.Trim())
            .NotNull()
            .WithMessage(ErrorConstants.NameNotNull)
            .MaximumLength(100)
            .WithMessage(ErrorConstants.ProductNameMaxLength)
            .Must(IsUniqueName)
            .WithMessage(ErrorConstants.NameUnique)
            .Matches(@"^[0-9a-zA-Z .,_\-=#:;%&*()]*$", RegexOptions.IgnoreCase)
            .WithMessage(ErrorConstants.ProductNameSymbols);
    }
    
    private bool IsUniqueName(string name)
    {
        var products = _cache.GetAccessKeys();

        return products?.FirstOrDefault(x =>
            x.DisplayName.Equals(name, StringComparison.InvariantCultureIgnoreCase)) == null;
    }

    protected override bool PreValidate(ValidationContext<string> context, ValidationResult result)
    {
        if (context.InstanceToValidate != null) return true;
        
        result.Errors.Add(new ValidationFailure("DisplayName", "Access key name shouldn't be null"));
        return false;
    }
}