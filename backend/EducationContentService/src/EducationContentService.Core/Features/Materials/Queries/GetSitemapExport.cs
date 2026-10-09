using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Materials.Queries;

/// <summary>
///     Метаданные-only выгрузка всех PUBLISHED материалов/подборок для
///     sitemap.xml (issue #469). Возвращается анонимно регардлесс access_type —
///     detail-страницы рендерятся для анонимов с замком, поэтому это легитимные
///     SEO-цели. Наружу идут только id/slug + updated_at: ни title, ни content
///     не утекают.
/// </summary>
public sealed record GetSitemapExportQuery : IQuery;

/// <summary>Sitemap-запись контента. Frontend-only read-DTO.</summary>
public sealed record SitemapEntryDto(Guid Id, DateTime UpdatedAt);

/// <summary>Полная выгрузка для app/sitemap.ts. Frontend-only read-DTO.</summary>
public sealed record SitemapExportDto(
    IReadOnlyList<SitemapEntryDto> Materials,
    IReadOnlyList<SitemapEntryDto> Collections);

public sealed class GetSitemapExportEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("materials/sitemap-export", async Task<EndpointResult<SitemapExportDto>> (
                    [FromServices] GetSitemapExportHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetSitemapExportQuery(), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetSitemapExportHandler : IQueryHandler<SitemapExportDto, GetSitemapExportQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetSitemapExportHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<SitemapExportDto> Handle(
        GetSitemapExportQuery query, CancellationToken cancellationToken = default)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT m.id, m.updated_at
                           FROM materials m
                           WHERE m.status = 'PUBLISHED'
                           ORDER BY m.id;

                           SELECT c.id, c.updated_at
                           FROM collections c
                           WHERE c.status = 'PUBLISHED'
                           ORDER BY c.id;
                           """;

        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(command);

        var materialRows = (await multi.ReadAsync<EntryRow>()).ToList();
        var collectionRows = (await multi.ReadAsync<EntryRow>()).ToList();

        return new SitemapExportDto(
            materialRows.Select(r => new SitemapEntryDto(r.Id, r.UpdatedAt)).ToList(),
            collectionRows.Select(r => new SitemapEntryDto(r.Id, r.UpdatedAt)).ToList());
    }

    private sealed class EntryRow
    {
        public Guid Id { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

}