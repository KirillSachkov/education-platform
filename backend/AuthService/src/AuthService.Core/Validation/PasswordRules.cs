using Core.Validation;
using FluentValidation;

namespace AuthService.Core.Validation;

public static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> IsValidPassword<T>(
        this IRuleBuilder<T, string> ruleBuilder, string fieldName = "password")
    {
        return ruleBuilder
            .MinimumLength(8).WithError(GeneralErrors.ValueIsInvalid(fieldName))
            .Matches("[A-Z]").WithError(GeneralErrors.ValueIsInvalid(fieldName))
            .Matches("[a-z]").WithError(GeneralErrors.ValueIsInvalid(fieldName))
            .Matches("[0-9]").WithError(GeneralErrors.ValueIsInvalid(fieldName))
            .Matches("[^a-zA-Z0-9]").WithError(GeneralErrors.ValueIsInvalid(fieldName));
    }
}
