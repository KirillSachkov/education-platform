using FileService.Domain;

namespace FileService.Core.Features.AssetRegistry;

internal static class AssetTargetRules
{
    public static UnitResult<Error> Validate(AssetUsageType usageType, TargetEntity? targetEntity)
    {
        Result<AssetUsagePolicy, Error> policyResult = AssetUsagePolicyCatalog.Get(usageType);
        if (policyResult.IsFailure)
        {
            return policyResult.Error;
        }

        AssetUsagePolicy policy = policyResult.Value;

        return policy.RegistrationMode switch
        {
            AssetRegistrationMode.DraftOrEntity => ValidateDraftOrEntity(policy, targetEntity),
            AssetRegistrationMode.DraftRequired when targetEntity is not null =>
                Error.Validation("asset.target.invalid", "Черновой ресурс не может быть привязан к сущности при загрузке"),
            AssetRegistrationMode.EntityRequired when targetEntity is null =>
                GeneralErrors.ValueIsRequired("targetEntity"),
            AssetRegistrationMode.EntityRequired when !policy.IsTargetTypeAllowed(targetEntity!.Type) =>
                Error.Validation("asset.target.invalid", "Тип целевой сущности не допускается для этого ресурса"),
            _ => UnitResult.Success<Error>()
        };
    }

    private static UnitResult<Error> ValidateDraftOrEntity(AssetUsagePolicy policy, TargetEntity? targetEntity)
    {
        if (targetEntity is not null && !policy.IsTargetTypeAllowed(targetEntity.Type))
        {
            return Error.Validation("asset.target.invalid", "Тип целевой сущности не допускается для этого ресурса");
        }

        return UnitResult.Success<Error>();
    }
}
