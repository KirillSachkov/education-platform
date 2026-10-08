using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record DeleteTopicCommand(Guid TopicId) : ICommand;

public sealed class DeleteTopicEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/trainer/topics/{topicId:guid}",
                async Task<EndpointResult> (
                    Guid topicId,
                    DeleteTopicHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new DeleteTopicCommand(topicId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Удаляет тему (admin/seed). <b>Block-on-children</b>: если к теме привязаны банки вопросов —
///     отбивает с описательной ошибкой (сначала отвяжите банки через DELETE .../banks/{bankId}).
///     Сами ECS-квизы при удалении банков не трогаются. Mastery/study-state остаются user-scoped
///     и не каскадятся (Wolverine/messaging в сервисе не подключены — Ф1).
/// </summary>
public sealed class DeleteTopicHandler : ICommandHandler<DeleteTopicCommand>
{
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public DeleteTopicHandler(
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITransactionManager transactions)
    {
        _topics = topics;
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        DeleteTopicCommand command,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => b.TopicId == command.TopicId, cancellationToken);
        if (banks.Count > 0)
            return TrainerServiceErrors.Topic.HasBanks(command.TopicId, banks.Count);

        await _topics.RemoveAsync(topicResult.Value, cancellationToken);

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
