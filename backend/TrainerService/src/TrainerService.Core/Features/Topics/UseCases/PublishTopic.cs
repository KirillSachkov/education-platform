using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record PublishTopicCommand(Guid TopicId) : ICommand;

public sealed class PublishTopicEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/topics/{topicId:guid}/publish",
                async Task<EndpointResult> (
                    Guid topicId,
                    PublishTopicHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new PublishTopicCommand(topicId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Публикует тему — делает её видимой в студенческом списке (admin/seed).</summary>
public sealed class PublishTopicHandler : ICommandHandler<PublishTopicCommand>
{
    private readonly ITopicsRepository _topics;
    private readonly ITransactionManager _transactions;

    public PublishTopicHandler(ITopicsRepository topics, ITransactionManager transactions)
    {
        _topics = topics;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        PublishTopicCommand command,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        topicResult.Value.Publish();

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
