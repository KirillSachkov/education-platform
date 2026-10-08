using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Core.Features.MockInterviews.Queries;

public sealed record GetMockInterviewsForManageQuery : IQuery;

public sealed class GetMockInterviewsForManageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/mock-interviews/manage",
                async Task<EndpointResult<IReadOnlyList<MockInterviewManageItemDto>>> (
                    GetMockInterviewsForManageHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMockInterviewsForManageQuery(), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Авторский список симуляций (включая DRAFT) для управления (admin): метаданные + размер
///     курированного набора (<c>QuestionCount</c>) + размер случайной подвыборки на сессию. #585.
/// </summary>
public sealed class GetMockInterviewsForManageHandler
    : IQueryHandlerWithResult<IReadOnlyList<MockInterviewManageItemDto>, GetMockInterviewsForManageQuery>
{
    private readonly IMockInterviewsRepository _mockInterviews;

    public GetMockInterviewsForManageHandler(IMockInterviewsRepository mockInterviews) =>
        _mockInterviews = mockInterviews;

    public async Task<Result<IReadOnlyList<MockInterviewManageItemDto>, Error>> Handle(
        GetMockInterviewsForManageQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MockInterview> interviews =
            await _mockInterviews.GetManyByAsync(_ => true, cancellationToken);

        return interviews
            .OrderBy(m => m.SortIndex)
            .ThenBy(m => m.CreatedAt)
            .Select(m => new MockInterviewManageItemDto(
                m.Id,
                m.Slug,
                m.Title,
                m.IsPublished,
                m.Questions.Count,
                m.QuestionsPerSession))
            .ToList();
    }
}
