using Core.Abstractions;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Core.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;

namespace MaterialProcessingService.Core.Features.Timecodes.GetArtifactStatuses;

public sealed record GetVideoArtifactStatusesQuery(IReadOnlyList<VideoArtifactQuery> Items) : IQuery;

public sealed class GetVideoArtifactStatusesValidator : AbstractValidator<GetVideoArtifactStatusesQuery>
{
    private const int MAX_ITEMS = 200;

    public GetVideoArtifactStatusesValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetVideoArtifactStatusesQuery.Items)));

        RuleFor(x => x.Items.Count)
            .LessThanOrEqualTo(MAX_ITEMS)
            .WithError(Error.Validation(
                "artifact.status.batch.too.large",
                $"В одном запросе допускается не более {MAX_ITEMS} видео."));

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.VideoId)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired(nameof(VideoArtifactQuery.VideoId)));
        });
    }
}

public sealed class GetVideoArtifactStatusesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Videos.MANAGE — author-tooling; handler фильтрует batch по RequestedByUserId,
        // чтобы один автор не мог перечислять наличие артефактов другого.
        app.MapPost("/material-processing/artifacts/by-videos/", async Task<EndpointResult<GetVideoArtifactStatusesResponse>> (
                [FromBody] GetVideoArtifactStatusesRequest request,
                [FromServices] IQueryHandlerWithResult<GetVideoArtifactStatusesResponse, GetVideoArtifactStatusesQuery> handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetVideoArtifactStatusesQuery(request.Items), cancellationToken))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }
}

public sealed class GetVideoArtifactStatusesHandler
    : IQueryHandlerWithResult<GetVideoArtifactStatusesResponse, GetVideoArtifactStatusesQuery>
{
    private readonly ITimecodeGenerationJobRepository _timecodeRepository;
    private readonly IVideoTranscriptRepository _transcriptRepository;
    private readonly IContentGenerationJobRepository _contentRepository;
    private readonly IValidator<GetVideoArtifactStatusesQuery> _validator;
    private readonly UserScopedData _user;

    public GetVideoArtifactStatusesHandler(
        ITimecodeGenerationJobRepository timecodeRepository,
        IVideoTranscriptRepository transcriptRepository,
        IContentGenerationJobRepository contentRepository,
        IValidator<GetVideoArtifactStatusesQuery> validator,
        UserScopedData user)
    {
        _timecodeRepository = timecodeRepository;
        _transcriptRepository = transcriptRepository;
        _contentRepository = contentRepository;
        _validator = validator;
        _user = user;
    }

    public async Task<Result<GetVideoArtifactStatusesResponse, Error>> Handle(
        GetVideoArtifactStatusesQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        if (query.Items.Count == 0)
            return new GetVideoArtifactStatusesResponse([]);

        IReadOnlyList<Guid> videoIds = query.Items
            .Select(x => x.VideoId)
            .Distinct()
            .ToList();

        IReadOnlyList<Guid> materialIds = query.Items
            .Where(x => x.MaterialId.HasValue)
            .Select(x => x.MaterialId!.Value)
            .Distinct()
            .ToList();

        Guid? requestedByUserId = _user.IsAdmin ? null : _user.UserId;

        IReadOnlySet<Guid> withTranscript =
            await _transcriptRepository.GetVideoIdsWithTranscriptAsync(
                videoIds,
                requestedByUserId,
                cancellationToken);
        IReadOnlySet<Guid> withTimecodes =
            await _timecodeRepository.GetVideoIdsWithCompletedTimecodesAsync(
                videoIds,
                requestedByUserId,
                cancellationToken);
        IReadOnlySet<Guid> withSummary =
            await _contentRepository.GetMaterialIdsWithCompletedSummaryAsync(
                materialIds,
                requestedByUserId,
                cancellationToken);

        List<VideoArtifactStatusDto> items = query.Items
            .Select(item => new VideoArtifactStatusDto(
                item.VideoId,
                item.MaterialId,
                HasTranscript: withTranscript.Contains(item.VideoId),
                HasTimecodes: withTimecodes.Contains(item.VideoId),
                HasSummary: item.MaterialId.HasValue && withSummary.Contains(item.MaterialId.Value)))
            .ToList();

        return new GetVideoArtifactStatusesResponse(items);
    }
}
