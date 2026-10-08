using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NotificationService.Contracts.Subscriptions.Dtos;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;
using PlatformAuth.Middleware;
using SharedKernel;

namespace NotificationService.Core.Features.Subscriptions.UseCases;

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/subscriptions", async Task<EndpointResult<SubscriptionsListResponse>> (
                [FromServices] ListMySubscriptionsHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new ListMySubscriptionsQuery(), cancellationToken))
            .RequireAuthorization();
    }
}

public sealed record ListMySubscriptionsQuery : IQuery;

public sealed class ListMySubscriptionsHandler
    : IQueryHandlerWithResult<SubscriptionsListResponse, ListMySubscriptionsQuery>
{
    private readonly ISubscriptionsRepository _subscriptionsRepository;
    private readonly UserScopedData _user;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;

    public ListMySubscriptionsHandler(
        ISubscriptionsRepository subscriptionsRepository,
        UserScopedData user,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient)
    {
        _subscriptionsRepository = subscriptionsRepository;
        _user = user;
        _ecsClient = ecsClient;
        _authClient = authClient;
    }

    public async Task<Result<SubscriptionsListResponse, Error>> Handle(
        ListMySubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = _user.UserId;

        IReadOnlyList<Subscription> subscriptions = await _subscriptionsRepository
            .ListBy(x => x.UserId == userId, cancellationToken);

        if (subscriptions.Count == 0)
            return new SubscriptionsListResponse { Items = [] };

        Dictionary<Guid, string> titleByEntityId = await ResolveTitlesAsync(
            subscriptions, cancellationToken);

        SubscriptionDto[] items = new SubscriptionDto[subscriptions.Count];
        for (int i = 0; i < subscriptions.Count; i++)
        {
            Subscription s = subscriptions[i];
            items[i] = new SubscriptionDto
            {
                Id = s.Id.Value,
                EntityType = s.EntityType,
                EntityId = s.EntityId,
                Title = titleByEntityId.GetValueOrDefault(s.EntityId),
                CreatedAt = new DateTimeOffset(s.CreatedAt, TimeSpan.Zero),
            };
        }

        return new SubscriptionsListResponse { Items = items };
    }

    private async Task<Dictionary<Guid, string>> ResolveTitlesAsync(
        IReadOnlyList<Subscription> subscriptions,
        CancellationToken ct)
    {
        Dictionary<Guid, string> titles = new(subscriptions.Count);

        // Course/module lookups go through cached ECS client one-by-one. HybridCache
        // (Redis L2 + memory L1) makes repeats free; first-time loads a few HTTP calls
        // for the settings page is acceptable.
        foreach (Subscription s in subscriptions)
        {
            if (string.Equals(s.EntityType, SubscriptionEntityType.COURSE, StringComparison.Ordinal))
            {
                Result<CourseSearchLookupDto, Error> lookup =
                    await _ecsClient.GetCourseSearchLookupAsync(s.EntityId, ct);
                if (lookup.IsSuccess && lookup.Value is not null)
                    titles[s.EntityId] = lookup.Value.Title;
            }
            else if (string.Equals(s.EntityType, SubscriptionEntityType.MODULE, StringComparison.Ordinal))
            {
                Result<ModuleSearchLookupDto, Error> lookup =
                    await _ecsClient.GetModuleSearchLookupAsync(s.EntityId, ct);
                if (lookup.IsSuccess && lookup.Value is not null)
                    titles[s.EntityId] = lookup.Value.Title;
            }
        }

        // Authors batch-resolve через AuthService — у клиента уже есть batch-by-ids
        // эндпоинт.
        Guid[] authorIds = subscriptions
            .Where(s => string.Equals(s.EntityType, SubscriptionEntityType.AUTHOR, StringComparison.Ordinal))
            .Select(s => s.EntityId)
            .Distinct()
            .ToArray();

        if (authorIds.Length > 0)
        {
            Result<IReadOnlyList<AuthUserLookupDto>, Error> lookup =
                await _authClient.GetUsersByIdsAsync(authorIds, ct);
            if (lookup.IsSuccess)
            {
                foreach (AuthUserLookupDto u in lookup.Value)
                {
                    string display = !string.IsNullOrWhiteSpace(u.Name) ? u.Name!
                        : !string.IsNullOrWhiteSpace(u.Username) ? u.Username!
                        : u.Email;
                    titles[u.UserId] = display;
                }
            }
        }

        return titles;
    }
}
