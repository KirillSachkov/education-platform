using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.AuthorCredit;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.HttpCommunication;

namespace EducationContentService.Core.Features.Materials.Queries;

/// <summary>
///     Публичный batch-эндпоинт «мета карточки»: просмотры + длительность видео по списку
///     материалов. Нужен базе знаний — её карточки строятся из search-документов Typesense,
///     где нет ни views, ни duration. Отдаёт только PUBLISHED-материалы; никакого контента
///     (ни preview, ни title) — чисто числовые метаданные, утечки нет. Issue #500.
/// </summary>
public sealed record GetMaterialsCardMetaQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetMaterialsCardMetaQueryValidator : AbstractValidator<GetMaterialsCardMetaQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetMaterialsCardMetaQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetMaterialsCardMetaQuery.Ids)))
            .Must(ids => ids is null || ids.Count <= MAX_BATCH_SIZE)
            .WithError(GeneralErrors.OutOfRange(nameof(GetMaterialsCardMetaQuery.Ids), min: 1, max: MAX_BATCH_SIZE));
    }
}

public sealed class GetMaterialsCardMetaEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Tier 1: anon ОК — metadata-only feed policy (см. root CLAUDE.md, Content Access
        // Control). Tier 3 не нужен: ни тела, ни заголовка не возвращаем; счётчик просмотров
        // и так публичный (рендерится на каталожных карточках), duration — промо-метаданные
        // уровня «описание курса». PUBLISHED-фильтр в SQL отсекает черновики.
        app.MapPost("materials/card-meta",
                async Task<EndpointResult<IReadOnlyList<MaterialCardMetaDto>>> (
                    [FromBody] GetMaterialsCardMetaRequest request,
                    [FromServices] GetMaterialsCardMetaHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialsCardMetaQuery(request.Ids), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetMaterialsCardMetaHandler
    : IQueryHandlerWithResult<IReadOnlyList<MaterialCardMetaDto>, GetMaterialsCardMetaQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetMaterialsCardMetaQuery> _validator;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;
    private readonly IAuthorLookupClient _authorLookupClient;

    public GetMaterialsCardMetaHandler(
        ITransactionManager transactionManager,
        IValidator<GetMaterialsCardMetaQuery> validator,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient,
        IAuthorLookupClient authorLookupClient)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
        _authorLookupClient = authorLookupClient;
    }

    public async Task<Result<IReadOnlyList<MaterialCardMetaDto>, Error>> Handle(
        GetMaterialsCardMetaQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        const string sql = """
                           SELECT id, author_id, video_id
                           FROM materials
                           WHERE id = ANY(@Ids)
                             AND status = 'PUBLISHED';
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        List<MaterialVideoRow> rows = (await connection.QueryAsync<MaterialVideoRow>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken))).ToList();

        if (rows.Count == 0)
        {
            return Result.Success<IReadOnlyList<MaterialCardMetaDto>, Error>([]);
        }

        List<Guid> videoIds = rows
            .Where(r => r.VideoId is not null)
            .Select(r => r.VideoId!.Value)
            .Distinct()
            .ToList();

        Task<Dictionary<Guid, GetPublicVideoResponse>> videosTask = videoIds.Count > 0
            ? MaterialFeedEnricher.LoadVideosAsync(_fileServiceClient, videoIds, cancellationToken)
            : Task.FromResult(new Dictionary<Guid, GetPublicVideoResponse>());

        // Soft-fail (как в фидах): счётчик не должен ронять карточки.
        Task<Result<IReadOnlyDictionary<Guid, long>, Error>> viewsTask =
            _progressServiceClient.GetMaterialViewsCountsAsync(rows.Select(r => r.Id).ToList(), cancellationToken);

        Dictionary<Guid, GetPublicVideoResponse> videoMap = await videosTask;
        Result<IReadOnlyDictionary<Guid, long>, Error> viewsResult = await viewsTask;
        IReadOnlyDictionary<Guid, long> viewsMap = viewsResult.IsSuccess
            ? viewsResult.Value
            : new Dictionary<Guid, long>();

        // Author credit (#569) — batch-resolve display name + avatar; best-effort, never blocks cards.
        IReadOnlyDictionary<Guid, AuthorCardCredit> authorMap = await ResolveAuthorsAsync(
            rows.Select(r => r.AuthorId).Distinct().ToList(), cancellationToken);

        List<MaterialCardMetaDto> result = rows.Select(r =>
        {
            GetPublicVideoResponse? video = r.VideoId is not null
                ? videoMap.GetValueOrDefault(r.VideoId.Value)
                : null;
            long views = viewsMap.TryGetValue(r.Id, out long c) ? c : 0L;
            AuthorCardCredit credit = authorMap.GetValueOrDefault(r.AuthorId);
            return new MaterialCardMetaDto(
                r.Id, views, video?.DurationSeconds, credit.DisplayName, credit.AvatarUrl);
        }).ToList();

        return Result.Success<IReadOnlyList<MaterialCardMetaDto>, Error>(result);
    }

    // Batch-resolves author display name + avatar URL for the card authors (#569). Both
    // best-effort — a degraded AuthService / FileService leaves the credit empty and never
    // fails the card-meta response. Mirrors the single avatar-batch round-trip used by GetCatalog.
    private async Task<IReadOnlyDictionary<Guid, AuthorCardCredit>> ResolveAuthorsAsync(
        IReadOnlyCollection<Guid> authorIds, CancellationToken cancellationToken)
    {
        if (authorIds.Count == 0)
            return new Dictionary<Guid, AuthorCardCredit>();

        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync(authorIds, cancellationToken);
        if (authorsResult.IsFailure)
            return new Dictionary<Guid, AuthorCardCredit>();

        IReadOnlyDictionary<Guid, AuthorCreditDto> authors = authorsResult.Value;

        List<Guid> avatarIds = authors.Values
            .Where(a => a.AvatarId is not null)
            .Select(a => a.AvatarId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> avatarUrlMap = [];
        if (avatarIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(avatarIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        avatarUrlMap[file.Id] = file.ContentUrl;
                }
            }
        }

        var result = new Dictionary<Guid, AuthorCardCredit>(authors.Count);
        foreach ((Guid authorId, AuthorCreditDto credit) in authors)
        {
            string? avatarUrl = credit.AvatarId is { } id
                ? avatarUrlMap.GetValueOrDefault(id)
                : null;
            result[authorId] = new AuthorCardCredit(credit.DisplayName, avatarUrl);
        }

        return result;
    }

    private readonly record struct AuthorCardCredit(string? DisplayName, string? AvatarUrl);

    private sealed record MaterialVideoRow(Guid Id, Guid AuthorId, Guid? VideoId);
}
