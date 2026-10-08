using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Materials.Queries;

public sealed record GetMaterialBindingsQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialBindingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // SECURITY: endpoint открыт для всех (включая анонимов) с #500 — блок «Содержится в»
        // на странице материала показывается каждому читателю для навигации на курс/подборку
        // (containment + upsell). Возвращаются ТОЛЬКО метаданные PUBLISHED сущностей
        // (title/slug — уже discoverable через каталог и поиск); DRAFT отфильтрован в SQL
        // ниже. Если endpoint когда-нибудь расширится до DRAFT/архивных данных — вернуть
        // permission-гейт.
        app.MapGet("materials/{materialId:guid}/bindings",
                async Task<EndpointResult<MaterialBindingsDto>> (
                    [FromRoute] Guid materialId,
                    [FromServices] GetMaterialBindingsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialBindingsQuery(materialId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

/// <summary>
///     Возвращает все привязки материала: курсы (course_materials), модули (module_items)
///     и подборки (collection_items). Используется:
///     - на странице материала — показать «Этот материал также в: курсе X, подборке Y»;
///     - в UI автора — перед удалением или сменой AccessType;
///     - для диагностики — audit где ссылка на материал.
///     Только PUBLISHED курсы/подборки; модули не фильтруются по статусу, т. к. они видимы
///     только вместе с курсом. Дубликатов не будет благодаря DISTINCT.
/// </summary>
public sealed class GetMaterialBindingsHandler
    : IQueryHandler<MaterialBindingsDto, GetMaterialBindingsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetMaterialBindingsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<MaterialBindingsDto> Handle(
        GetMaterialBindingsQuery query,
        CancellationToken cancellationToken = default)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string coursesSql = """
                                  SELECT DISTINCT
                                      c.id       AS course_id,
                                      c.title    AS title,
                                      c.slug     AS slug,
                                      c.author_id
                                  FROM course_materials cm
                                  JOIN courses c ON c.id = cm.course_id
                                  WHERE cm.material_id = @MaterialId
                                    AND c.status = 'PUBLISHED'
                                  ORDER BY c.title;
                                  """;

        // SECURITY: фильтр по статусу модуля обязателен — DRAFT-модули (даже внутри PUBLISHED курса)
        // не должны раскрывать своё имя/ID анонимным вызывателям этого endpoint'а.
        const string modulesSql = """
                                  SELECT DISTINCT
                                      mod.id      AS module_id,
                                      mod.title   AS title,
                                      c.id        AS course_id,
                                      c.title     AS course_title,
                                      c.slug      AS course_slug
                                  FROM module_items mi
                                  JOIN modules mod ON mod.id = mi.module_id
                                  JOIN course_items ci ON ci.reference_id = mod.id AND ci.item_type = 'Module'
                                  JOIN courses c ON c.id = ci.course_id
                                  WHERE mi.item_type = 'Material'
                                    AND mi.reference_id = @MaterialId
                                    AND c.status = 'PUBLISHED'
                                    AND mod.status = 'PUBLISHED'
                                  ORDER BY mod.title;
                                  """;

        const string collectionsSql = """
                                      SELECT DISTINCT
                                          col.id         AS collection_id,
                                          col.title      AS title,
                                          col.course_id  AS course_id,
                                          c.slug         AS course_slug
                                      FROM collection_items ci
                                      JOIN collection_sections cs ON cs.id = ci.section_id
                                      JOIN collections col ON col.id = cs.collection_id
                                      LEFT JOIN courses c ON c.id = col.course_id
                                      WHERE ci.item_type = 'MATERIAL'
                                        AND ci.reference_id = @MaterialId
                                        AND col.status = 'PUBLISHED'
                                      ORDER BY col.title;
                                      """;

        var parameters = new { query.MaterialId };

        // Отправляем все 3 SELECT'а одним CommandBatch'ом и читаем 3 result-set'а через GridReader.
        // PostgreSQL выполняет их последовательно, но мы экономим 2 из 3 network roundtrip'ов.
        // Параллельное выполнение на одном DbConnection невозможно (Npgsql не поддерживает MARS).
        string batchSql = string.Join(";\n", coursesSql, modulesSql, collectionsSql);

        using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(batchSql, parameters, cancellationToken: cancellationToken));

        var courses = (await multi.ReadAsync<MaterialCourseBindingDto>()).ToList();
        var modules = (await multi.ReadAsync<MaterialModuleBindingDto>()).ToList();
        var collections = (await multi.ReadAsync<MaterialCollectionBindingDto>()).ToList();

        return new MaterialBindingsDto(query.MaterialId, courses, modules, collections);
    }
}
