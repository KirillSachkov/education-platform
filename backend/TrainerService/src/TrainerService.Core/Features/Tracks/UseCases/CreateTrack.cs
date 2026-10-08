using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Tracks;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Tracks;

namespace TrainerService.Core.Features.Tracks.UseCases;

public sealed record CreateTrackCommand(
    string? Slug,
    string? Title,
    string? Stack,
    string? Description) : ICommand;

public sealed class CreateTrackCommandValidator : AbstractValidator<CreateTrackCommand>
{
    public CreateTrackCommandValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTrackCommand.Slug)));

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTrackCommand.Title)));

        RuleFor(x => x.Stack)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTrackCommand.Stack)));
    }
}

public sealed class CreateTrackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/tracks",
                async Task<EndpointResult<TrackIdResponse>> (
                    CreateTrackRequest request,
                    CreateTrackHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CreateTrackCommand(
                            request.Slug,
                            request.Title,
                            request.Stack,
                            request.Description),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Создаёт трек тренажёра (admin/seed). Slug уникален. Трек стартует как DRAFT —
///     публикуется отдельным эндпоинтом. Sort key — append к концу списка треков.
/// </summary>
public sealed class CreateTrackHandler : ICommandHandler<TrackIdResponse, CreateTrackCommand>
{
    private readonly IValidator<CreateTrackCommand> _validator;
    private readonly ITracksRepository _tracks;
    private readonly ITransactionManager _transactions;

    public CreateTrackHandler(
        IValidator<CreateTrackCommand> validator,
        ITracksRepository tracks,
        ITransactionManager transactions)
    {
        _validator = validator;
        _tracks = tracks;
        _transactions = transactions;
    }

    public async Task<Result<TrackIdResponse, Error>> Handle(
        CreateTrackCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        if (!TrackStackParser.TryParse(command.Stack, out TrackStack stack))
            return TrainerServiceErrors.Track.InvalidStack(command.Stack!);

        string slug = command.Slug!.Trim();
        bool slugTaken = await _tracks.ExistsAsync(t => t.Slug == slug, cancellationToken);
        if (slugTaken)
            return TrainerServiceErrors.Track.SlugAlreadyExists(slug);

        string sortKey = await ComputeAppendSortKeyAsync(cancellationToken);

        Result<Track, Error> trackResult = Track.Create(
            command.Slug,
            command.Title,
            stack,
            command.Description,
            sortKey);
        if (trackResult.IsFailure)
            return trackResult.Error;

        Track track = trackResult.Value;
        await _tracks.AddAsync(track, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new TrackIdResponse(track.Id);
    }

    private async Task<string> ComputeAppendSortKeyAsync(CancellationToken ct)
    {
        string? maxKey = await _tracks.GetMaxSortKeyAsync(ct);
        if (maxKey is null)
            return SortKey.Initial().Value;

        Result<SortKey, Error> parsed = SortKey.Create(maxKey);
        return parsed.IsSuccess ? SortKey.After(parsed.Value).Value : SortKey.Initial().Value;
    }
}
