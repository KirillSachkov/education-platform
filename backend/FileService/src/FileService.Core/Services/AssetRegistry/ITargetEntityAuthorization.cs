using FileService.Domain;

namespace FileService.Core.Services.AssetRegistry;

public interface ITargetEntityAuthorization
{
    Task<UnitResult<Error>> AuthorizeAsync(TargetEntity targetEntity, CancellationToken cancellationToken);

    Task<UnitResult<Error>> AuthorizeManagerAsync(
        TargetEntity targetEntity,
        CancellationToken cancellationToken);
}
