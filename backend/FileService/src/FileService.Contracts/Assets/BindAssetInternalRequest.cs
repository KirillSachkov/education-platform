using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

/// <summary>
///     Доверенная S2S-привязка для ECS create-flow, когда target ещё не закоммичен
///     и обратный ownership lookup не может его увидеть. FileService всё равно
///     проверяет владельца нового asset по <see cref="ActorUserId"/>.
/// </summary>
public sealed record BindAssetInternalRequest(
    TargetEntityDto TargetEntity,
    Guid ActorUserId,
    bool ActorCanManageAnyAsset,
    Guid? SelectionId = null);
