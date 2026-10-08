using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.Queries;

public sealed record GetTopicForManageQuery(Guid TopicId) : IQuery;

public sealed class GetTopicForManageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/topics/{topicId:guid}/manage",
                async Task<EndpointResult<TopicAdminDto>> (
                    Guid topicId,
                    GetTopicForManageHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetTopicForManageQuery(topicId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Admin-карточка темы по id (включая DRAFT) — полные метаданные + счётчик банков.</summary>
public sealed class GetTopicForManageHandler : IQueryHandlerWithResult<TopicAdminDto, GetTopicForManageQuery>
{
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;

    public GetTopicForManageHandler(ITopicsRepository topics, ITopicBanksRepository banks)
    {
        _topics = topics;
        _banks = banks;
    }

    public async Task<Result<TopicAdminDto, Error>> Handle(
        GetTopicForManageQuery query,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == query.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(query.TopicId);

        Topic topic = topicResult.Value;

        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => b.TopicId == topic.Id, cancellationToken);

        return new TopicAdminDto(
            topic.Id,
            topic.TrackId,
            topic.Slug,
            topic.Title,
            topic.Area,
            topic.Description,
            topic.Direction?.ToString(),
            topic.RecommendedCourseId,
            topic.FallbackCourseId,
            topic.SortKey,
            topic.IsPublished,
            banks.Count,
            topic.CreatedAt,
            topic.UpdatedAt);
    }
}
