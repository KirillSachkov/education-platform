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
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Topics;
using TrainerService.Domain;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record CreateTopicCommand(
    Guid TrackId,
    string? Slug,
    string? Title,
    string? Area,
    string? Description,
    string? Direction,
    Guid? RecommendedCourseId,
    Guid? FallbackCourseId) : ICommand;

public sealed class CreateTopicCommandValidator : AbstractValidator<CreateTopicCommand>
{
    public CreateTopicCommandValidator()
    {
        RuleFor(x => x.TrackId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTopicCommand.TrackId)));

        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTopicCommand.Slug)));

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTopicCommand.Title)));

        RuleFor(x => x.Area)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateTopicCommand.Area)));
    }
}

public sealed class CreateTopicEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/topics",
                async Task<EndpointResult<TopicIdResponse>> (
                    CreateTopicRequest request,
                    CreateTopicHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CreateTopicCommand(
                            request.TrackId,
                            request.Slug,
                            request.Title,
                            request.Area,
                            request.Description,
                            request.Direction,
                            request.RecommendedCourseId,
                            request.FallbackCourseId),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Создаёт тему тренажёра (admin/seed). Slug уникален. Тема стартует как DRAFT —
///     публикуется отдельным эндпоинтом. Sort key — append к концу списка тем.
/// </summary>
public sealed class CreateTopicHandler : ICommandHandler<TopicIdResponse, CreateTopicCommand>
{
    private readonly IValidator<CreateTopicCommand> _validator;
    private readonly ITracksRepository _tracks;
    private readonly ITopicsRepository _topics;
    private readonly ITransactionManager _transactions;

    public CreateTopicHandler(
        IValidator<CreateTopicCommand> validator,
        ITracksRepository tracks,
        ITopicsRepository topics,
        ITransactionManager transactions)
    {
        _validator = validator;
        _tracks = tracks;
        _topics = topics;
        _transactions = transactions;
    }

    public async Task<Result<TopicIdResponse, Error>> Handle(
        CreateTopicCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        bool trackExists = await _tracks.ExistsAsync(t => t.Id == command.TrackId, cancellationToken);
        if (!trackExists)
            return TrainerServiceErrors.Track.NotFound(command.TrackId);

        Result<TopicDirection?, Error> directionResult = TopicDirectionParser.Parse(command.Direction);
        if (directionResult.IsFailure)
            return directionResult.Error;

        string slug = command.Slug!.Trim();
        bool slugTaken = await _topics.ExistsAsync(t => t.Slug == slug, cancellationToken);
        if (slugTaken)
            return TrainerServiceErrors.Topic.SlugAlreadyExists(slug);

        string sortKey = await ComputeAppendSortKeyAsync(cancellationToken);

        Result<Topic, Error> topicResult = Topic.Create(
            command.TrackId,
            command.Slug,
            command.Title,
            command.Area,
            command.Description,
            directionResult.Value,
            sortKey);
        if (topicResult.IsFailure)
            return topicResult.Error;

        Topic topic = topicResult.Value;
        if (command.RecommendedCourseId.HasValue || command.FallbackCourseId.HasValue)
            topic.SetRecommendedCourses(command.RecommendedCourseId, command.FallbackCourseId);

        await _topics.AddAsync(topic, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new TopicIdResponse(topic.Id);
    }

    private async Task<string> ComputeAppendSortKeyAsync(CancellationToken ct)
    {
        string? maxKey = await _topics.GetMaxSortKeyAsync(ct);
        if (maxKey is null)
            return SortKey.Initial().Value;

        Result<SortKey, Error> parsed = SortKey.Create(maxKey);
        return parsed.IsSuccess ? SortKey.After(parsed.Value).Value : SortKey.Initial().Value;
    }
}
