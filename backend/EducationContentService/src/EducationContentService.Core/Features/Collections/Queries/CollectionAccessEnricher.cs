using ContentAccess;
using EducationContentService.Core.Features.ContentAccess;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.Queries;

/// <summary>
///     Обогащает коллекцию строк <c>(accessType, courseId, items[])</c> per-row
///     <c>IsAccessible</c> и <c>LockReason</c>.
///     Один round-trip в Redis за user grants, дальше CPU-работа через
///     <see cref="LockReasonResolver"/>.
///
///     <para>
///     Bифуркация замка (для UI-карточки):
///     <list type="bullet">
///         <item>
///             Если сам header подборки доступен (PUBLIC, REGISTERED+auth, или enrollment-overlap)
///             — <c>IsAccessible=true</c>, <c>LockReason=null</c>. Карточка без замка.
///         </item>
///         <item>
///             Иначе если <b>хотя бы один</b> item внутри доступен текущему пользователю —
///             <c>IsAccessible=true</c>, <c>LockReason=null</c>. Карточка без замка, юзер
///             откроет detail и увидит per-item замки на гейтнутых материалах.
///         </item>
///         <item>
///             Иначе (ни header, ни итемы недоступны) — замок с lockReason от header'а.
///         </item>
///     </list>
///     </para>
/// </summary>
public sealed class CollectionAccessEnricher
{
    private readonly IEntitlementReader _entitlementReader;
    private readonly UserScopedData _user;
    private readonly ILogger<CollectionAccessEnricher> _logger;

    public CollectionAccessEnricher(
        IEntitlementReader entitlementReader,
        UserScopedData user,
        ILogger<CollectionAccessEnricher> logger)
    {
        _entitlementReader = entitlementReader;
        _user = user;
        _logger = logger;
    }

    /// <summary>
    ///     Обогащает каждый <paramref name="rows"/> access-метками. Админу всегда
    ///     <c>IsAccessible=true</c>. Анонимы получают только <c>access:public</c>.
    ///     Авторизованные — PUBLIC + AUTHENTICATED + персональные grants из Redis.
    /// </summary>
    public async Task<IReadOnlyList<CollectionAccessResult>> EnrichAsync(
        IReadOnlyList<CollectionAccessRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return [];

        if (_user.IsAdmin)
        {
            return rows
                .Select(r => new CollectionAccessResult(r.CollectionId, IsAccessible: true, LockReason: null))
                .ToArray();
        }

        List<string> effectiveGrants = [GrantTags.PUBLIC];
        if (_user.IsAuthenticated)
        {
            effectiveGrants.Add(GrantTags.AUTHENTICATED);

            EntitlementGrantSet userGrants =
                await _entitlementReader.GetUserGrantTagsAsync(_user.UserId, cancellationToken);

            effectiveGrants.AddRange(userGrants.Tags);
        }

        var grants = new EntitlementGrantSet(
            effectiveGrants.Distinct(StringComparer.Ordinal).ToArray());

        var results = new List<CollectionAccessResult>(rows.Count);
        foreach (CollectionAccessRow row in rows)
        {
            // Автору свои подборки всегда отдаются без замка — даже DRAFT'ы он видит в админке.
            if (_user.IsAuthenticated && row.AuthorId == _user.UserId)
            {
                results.Add(new CollectionAccessResult(row.CollectionId, IsAccessible: true, LockReason: null));
                continue;
            }

            // Defence-in-depth: пустой/null AccessType в строке = баг в SQL (забыли c.access_type
            // в SELECT). Fail-closed — помечаем как locked_not_enrolled, лучше показать замок
            // чем отдать доступ по ошибке, а warning в лог подскажет на query regression.
            if (string.IsNullOrWhiteSpace(row.AccessType))
            {
                _logger.LogWarning(
                    "CollectionAccessRow.AccessType is empty for collection {CollectionId} — check SQL projection",
                    row.CollectionId);
                results.Add(new CollectionAccessResult(
                    row.CollectionId, IsAccessible: false, LockReason: LockReasons.NOT_ENROLLED));
                continue;
            }

            // Сначала — проверка самого header'а подборки.
            Guid[] headerCourseIds = row.CourseId is null ? [] : [row.CourseId.Value];
            IReadOnlyList<string> headerTags = ContentAccessTagBuilder.Build(
                row.AccessType, row.CollectionId, headerCourseIds, _logger);

            AccessLockResult headerDecision = LockReasonResolver.Resolve(
                headerTags, grants, _user.IsAuthenticated);

            if (headerDecision.IsAccessible)
            {
                results.Add(new CollectionAccessResult(
                    row.CollectionId, IsAccessible: true, LockReason: null));
                continue;
            }

            // Header заблокирован — проверяем, виден ли хоть один item.
            // Если да — снимаем замок с карточки, юзер увидит per-item замки в detail.
            bool anyItemVisible = HasAnyAccessibleItem(row.Items, grants, _user.IsAuthenticated);
            if (anyItemVisible)
            {
                results.Add(new CollectionAccessResult(
                    row.CollectionId, IsAccessible: true, LockReason: null));
                continue;
            }

            // Полностью заблокирована — оставляем lockReason от header'а.
            results.Add(new CollectionAccessResult(
                row.CollectionId, IsAccessible: false, LockReason: headerDecision.LockReason));
        }

        return results;
    }

    /// <summary>
    ///     Проверяет, есть ли среди items подборки хотя бы один, который доступен
    ///     текущему пользователю. Используется для решения «снимать ли замок с
    ///     карточки, если header гейтнут».
    /// </summary>
    private static bool HasAnyAccessibleItem(
        IReadOnlyList<CollectionItemAccessRow> items,
        EntitlementGrantSet grants,
        bool isAuthenticated)
    {
        foreach (CollectionItemAccessRow item in items)
        {
            if (string.IsNullOrWhiteSpace(item.AccessType))
                continue;

            IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
                item.AccessType, item.ReferenceId, item.CourseIds);

            // isAuthenticated влияет только на возвращаемый LockReason при отказе
            // (anonymous vs not_enrolled), но не на сам IsAccessible. Передаём
            // реальное значение явно — чтобы не зависеть от accidental correctness.
            AccessLockResult decision = LockReasonResolver.Resolve(tags, grants, isAuthenticated);

            if (decision.IsAccessible)
                return true;
        }
        return false;
    }
}

public readonly record struct CollectionAccessRow(
    Guid CollectionId,
    Guid AuthorId,
    string AccessType,
    Guid? CourseId,
    IReadOnlyList<CollectionItemAccessRow> Items);

/// <summary>
///     Срез доступа по item-у подборки (generic после #491: MATERIAL | QUIZ).
///     <see cref="ItemType"/> + <see cref="ReferenceId"/> — generic-ссылка; решающие поля —
///     <see cref="AccessType"/>, <see cref="CourseIds"/> (для MATERIAL — из
///     <c>course_materials</c>, для QUIZ — из <c>course_quizzes</c>) и
///     <see cref="AuthorId"/> (нужен для plan-tags автора у orphan-сущностей).
/// </summary>
public readonly record struct CollectionItemAccessRow(
    string ItemType,
    Guid ReferenceId,
    string AccessType,
    IReadOnlyList<Guid> CourseIds,
    Guid AuthorId);

public readonly record struct CollectionAccessResult(
    Guid CollectionId,
    bool IsAccessible,
    string? LockReason);
