using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace EducationContentService.Core.Features.Plans;

public sealed class PlanCourseEventsHandler
{
    private readonly HybridCache _cache;

    public PlanCourseEventsHandler(HybridCache cache)
    {
        _cache = cache;
    }

    public Task Handle(PlanCourseBound message, CancellationToken ct) =>
        _cache.RemoveAsync($"{CachedCoursePricingClient.CACHE_KEY_PREFIX}{message.CourseId}", ct).AsTask();

    public Task Handle(PlanCourseUnbound message, CancellationToken ct) =>
        _cache.RemoveAsync($"{CachedCoursePricingClient.CACHE_KEY_PREFIX}{message.CourseId}", ct).AsTask();
}
