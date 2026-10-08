using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Topics;
using TrainerService.Domain;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record UpdateTopicCommand(
    Guid TopicId,
    Guid TrackId,
    string? Title,
    string? Area,
    string? Description,
    string? Direction,
    Guid? RecommendedCourseId,
    Guid? FallbackCourseId) : ICommand;

public sealed class UpdateTopicCommandValidator : AbstractValidator<UpdateTopicCommand>
{
    public UpdateTopicCommandValidator()
    {
        RuleFor(x => x.TrackId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateTopicCommand.TrackId)));
    }
}

public sealed class UpdateTopicEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/trainer/topics/{topicId:guid}",
                async Task<EndpointResult> (
                    Guid topicId,
                    UpdateTopicRequest request,
                    UpdateTopicHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new UpdateTopicCommand(
                            topicId,
                            request.TrackId,
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

/// <summary>Обновляет детали темы + трек/направление + маппинг на рекомендуемый/fallback курс (admin/seed).</summary>
public sealed class UpdateTopicHandler : ICommandHandler<UpdateTopicCommand>
{
    private readonly IValidator<UpdateTopicCommand> _validator;
    private readonly ITracksRepository _tracks;
    private readonly ITopicsRepository _topics;
    private readonly ITransactionManager _transactions;

    public UpdateTopicHandler(
        IValidator<UpdateTopicCommand> validator,
        ITracksRepository tracks,
        ITopicsRepository topics,
        ITransactionManager transactions)
    {
        _validator = validator;
        _tracks = tracks;
        _topics = topics;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        UpdateTopicCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        bool trackExists = await _tracks.ExistsAsync(t => t.Id == command.TrackId, cancellationToken);
        if (!trackExists)
            return TrainerServiceErrors.Track.NotFound(command.TrackId);

        Result<TopicDirection?, Error> directionResult = TopicDirectionParser.Parse(command.Direction);
        if (directionResult.IsFailure)
            return directionResult.Error;

        Topic topic = topicResult.Value;

        UnitResult<Error> updateResult = topic.UpdateDetails(command.Title, command.Area, command.Description);
        if (updateResult.IsFailure)
            return updateResult.Error;

        UnitResult<Error> trackResult = topic.SetTrackAndDirection(command.TrackId, directionResult.Value);
        if (trackResult.IsFailure)
            return trackResult.Error;

        topic.SetRecommendedCourses(command.RecommendedCourseId, command.FallbackCourseId);

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
