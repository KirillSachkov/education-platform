using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record DeleteMaterialCommand(Guid MaterialId) : ICommand;

public sealed class DeleteMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("materials/{materialId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid materialId,
                    [FromServices] DeleteMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteMaterialCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class DeleteMaterialHandler : ICommandHandler<Guid, DeleteMaterialCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<DeleteMaterialHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public DeleteMaterialHandler(
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<DeleteMaterialHandler> logger,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        DeleteMaterialCommand command,
        CancellationToken cancellationToken)
    {
        Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == command.MaterialId,
            cancellationToken);
        if (materialResult.IsFailure)
            return materialResult.Error;

        Material material = materialResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(material.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        // Атомарная транзакция: либо все ссылки + сама запись сносятся вместе с outbox-event,
        // либо ничего. Без явного Begin/Commit Dapper-стейтменты прошли бы в autocommit и при
        // последующем падении SaveChangesAsync остались бы коммитнутыми (orphan-cleanup без удалённого материала).
        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        // Cascade-detach внутри ECS: hard-delete материала чистит все ссылки.
        // - module_items / course_materials — без FK, иначе orphan'ы.
        // - collection_items — generic-ссылка (#491, FK снят) — без cleanup'а orphan'ы.
        // - quizzes НЕ каскадятся (#489): квиз — standalone-сущность, ссылка живёт на
        //   материале (materials.quiz_id) и исчезает вместе с его строкой; сам квиз живёт.
        // - issues.internal_materials — JSONB-массив value object'ов. Ключи (`ReferenceId`,
        //   `ItemType`) — PascalCase из EF Core ToJson() default'а; если в DbContext добавят
        //   JsonNamingPolicy.CamelCase, JSONB-фильтр станет no-op'ом — обновлять обе стороны вместе.
        DbConnection connection = _transactionManager.GetDbConnection();
        const string cascadeSql = """
            DELETE FROM module_items
            WHERE item_type = 'Material' AND reference_id = @MaterialId;

            DELETE FROM course_materials
            WHERE material_id = @MaterialId;

            DELETE FROM collection_items
            WHERE item_type = 'MATERIAL' AND reference_id = @MaterialId;

            UPDATE issues
            SET internal_materials = COALESCE((
                SELECT jsonb_agg(elem)
                FROM jsonb_array_elements(internal_materials) elem
                WHERE NOT (elem->>'ReferenceId' = @MaterialIdText
                           AND elem->>'ItemType'   = 'Material')
            ), '[]'::jsonb)
            WHERE internal_materials @> jsonb_build_array(
                jsonb_build_object('ReferenceId', @MaterialIdText, 'ItemType', 'Material'));

            """;
        await connection.ExecuteAsync(
            new CommandDefinition(
                cascadeSql,
                new { MaterialId = material.Id, MaterialIdText = material.Id.ToString() },
                cancellationToken: cancellationToken));

        _materialsRepository.Delete(material);

        await _outbox.PublishAsync(new MaterialHardDeleted(material.Id));

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        _logger.LogInformation("Material {MaterialId} hard-deleted", material.Id);

        return material.Id;
    }
}