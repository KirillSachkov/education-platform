using ContentAccess;
using PlatformAuth.Middleware;

namespace SearchService.Core.Features.EducationDocuments.Queries;

/// <summary>
/// Строит Typesense-фильтр выдачи + подгружает user grants для per-hit enrichment
/// (IsAccessible + LockReason). Поиск показывает ВСЁ опубликованное, недоступный
/// контент помечается замком — политика metadata-only feeds (CLAUDE.md).
/// Admin получает выдачу без scope-фильтра и с полным доступом.
/// </summary>
public sealed class EducationSearchAccessFilterBuilder
{
    private readonly IEntitlementReader _entitlementReader;
    private readonly UserScopedData _user;

    public EducationSearchAccessFilterBuilder(
        IEntitlementReader entitlementReader,
        UserScopedData user)
    {
        _entitlementReader = entitlementReader;
        _user = user;
    }

    /// <summary>
    /// Строит Typesense-фильтр (только scope: is_deleted + optional courseId) и
    /// возвращает grants пользователя — ими enrich'ится каждый hit на уровне handler'а.
    /// </summary>
    public async Task<SearchAccessContext> BuildAsync(
        Guid? courseId,
        CancellationToken cancellationToken = default)
    {
        // Admin search is the moderation/debug surface and intentionally includes
        // draft/deleted projection rows. Everyone else sees published rows only.
        List<string> filters = _user.IsAdmin ? [] : ["is_deleted:=false"];

        if (courseId.HasValue)
        {
            filters.Add($"course_id:=`{courseId.Value:D}`");
        }

        string filter = string.Join(" && ", filters);

        if (_user.IsAdmin)
        {
            return new SearchAccessContext(filter, EntitlementGrantSet.Empty, IsAdmin: true);
        }

        // GrantTags.PUBLIC добавляем в grants каждому пользователю, включая анонимов:
        // ContentAccessTagBuilder маркирует PUBLIC-ресурсы тегом "access:public" (НЕ пустым
        // списком), и без этого гранта матч не случится и резолвер вернёт locked для PUBLIC
        // материалов. AUTHENTICATED добавляем для REGISTERED-ресурсов — Redis user tag set
        // не обязан содержать его, это implementation detail EntitlementChecker.
        List<string> effectiveGrants = [GrantTags.PUBLIC];

        if (_user.IsAuthenticated)
        {
            effectiveGrants.Add(GrantTags.AUTHENTICATED);

            EntitlementGrantSet userGrants =
                await _entitlementReader.GetUserGrantTagsAsync(_user.UserId, cancellationToken);

            effectiveGrants.AddRange(userGrants.Tags);
        }

        string[] distinctGrants = effectiveGrants
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new SearchAccessContext(
            filter,
            new EntitlementGrantSet(distinctGrants),
            IsAdmin: false);
    }
}

/// <summary>
/// Результат построения фильтра + контекст для per-hit enrichment.
/// </summary>
public sealed record SearchAccessContext(
    string TypesenseFilter,
    EntitlementGrantSet UserGrants,
    bool IsAdmin);
