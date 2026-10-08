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

public sealed record UnpublishTopicCommand(Guid TopicId) : ICommand;

public sealed class UnpublishTopicEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/topics/{topicId:guid}/unpublish",
                async Task<EndpointResult> (
                    Guid topicId,
                    UnpublishTopicHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new UnpublishTopicCommand(topicId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Снимает тему с публикации — прячет её из студенческого списка (admin/seed). Идемпотентно.</summary>
public sealed class UnpublishTopicHandler : ICommandHandler<UnpublishTopicCommand>
{
    private readonly ITopicsRepository _topics;
    private readonly ITransactionManager _transactions;

    public UnpublishTopicHandler(ITopicsRepository topics, ITransactionManager transactions)
    {
        _topics = topics;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        UnpublishTopicCommand command,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        topicResult.Value.Unpublish();

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
