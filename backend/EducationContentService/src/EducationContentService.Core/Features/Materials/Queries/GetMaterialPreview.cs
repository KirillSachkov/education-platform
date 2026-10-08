using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Materials.Queries;

public sealed record GetMaterialPreviewQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialPreviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("materials/{materialId:guid}/preview", async Task<EndpointResult<MaterialPreviewDto>> (
                    [FromRoute] Guid materialId,
                    [FromServices] GetMaterialPreviewHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialPreviewQuery(materialId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

/// <summary>
///     Облегчённое превью PUBLISHED-материала: title + kind + cover.
///     Возвращается анонимно регардлесс entitlement-state (по аналогии с
///     <c>GetCollectionDetail</c> partial-access моделью) — нужно для OG/Twitter
///     карточек шаринга в соцсети. Body материала наружу не утекает.
/// </summary>
public sealed class GetMaterialPreviewHandler : IQueryHandlerWithResult<MaterialPreviewDto, GetMaterialPreviewQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly ILogger<GetMaterialPreviewHandler> _logger;

    public GetMaterialPreviewHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        ILogger<GetMaterialPreviewHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _logger = logger;
    }

    public async Task<Result<MaterialPreviewDto, Error>> Handle(
        GetMaterialPreviewQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               m.id,
                               m.author_id,
                               m.title,
                               m.kind,
                               m.access_type,
                               m.image_id,
                               m.published_at,
                               m.updated_at
                           FROM materials m
                           WHERE m.id = @MaterialId
                             AND m.status = 'PUBLISHED';
                           """;

        MaterialPreviewRow? row = await connection.QueryFirstOrDefaultAsync<MaterialPreviewRow>(
            new CommandDefinition(sql, new { query.MaterialId }, cancellationToken: cancellationToken));

        if (row is null)
            return EducationErrors.MaterialNotFound(query.MaterialId);

        string? imageUrl = null;
        if (row.ImageId is not null)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(row.ImageId.Value, cancellationToken);

            if (fileResult is { IsSuccess: true, Value: not null })
            {
                imageUrl = fileResult.Value.ContentUrl;
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch preview image {ImageId} for material {MaterialId}",
                    row.ImageId, query.MaterialId);
            }
        }

        return new MaterialPreviewDto(
            row.Id,
            row.AuthorId,
            row.Title,
            row.Kind,
            row.AccessType,
            row.ImageId,
            imageUrl,
            row.PublishedAt,
            row.UpdatedAt);
    }

    private sealed class MaterialPreviewRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public DateTime? PublishedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
