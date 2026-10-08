using System.Data.Common;
using Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

public sealed record ResolveMaterialTargetsQuery(IReadOnlyCollection<MaterialResolveRequestItem> Items) : IQuery;

public sealed class ResolveMaterialTargetsQueryValidator : AbstractValidator<ResolveMaterialTargetsQuery>
{
    public ResolveMaterialTargetsQueryValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .NotEmpty()
            .Must(items => items.Count <= 100)
            .WithMessage("Количество элементов не должно превышать 100");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.CourseId)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired(nameof(MaterialResolveRequestItem.CourseId)));
            item.RuleFor(x => x.Target)
                .MustBeValueObject(target => ResolvableMaterialEntityReference.Of(target.Type, target.Id));
        });
    }
}

public sealed class ResolveMaterialTargetsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/progress/materials/resolve",
                async Task<EndpointResult<IReadOnlyCollection<ResolvedMaterialDto>>> (
                    [FromBody] ResolveMaterialTargetsRequest request,
                    [FromServices] ResolveMaterialTargetsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new ResolveMaterialTargetsQuery(request.Items), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ResolveMaterialTargetsHandler
    : IQueryHandlerWithResult<IReadOnlyCollection<ResolvedMaterialDto>, ResolveMaterialTargetsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ResolveMaterialTargetsQuery> _validator;

    public ResolveMaterialTargetsHandler(
        ITransactionManager transactionManager,
        IValidator<ResolveMaterialTargetsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyCollection<ResolvedMaterialDto>, Error>> Handle(
        ResolveMaterialTargetsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        MaterialResolveRequestItem[] requestedItems = query.Items
            .Distinct()
            .ToArray();

        DbConnection connection = _transactionManager.GetDbConnection();
        var requestedKeys = requestedItems
            .Select(x => (x.CourseId, x.Target.Type, x.Target.Id))
            .ToHashSet();

        List<ResolvedMaterialDto> resolvedItems = [];

        MaterialResolveRequestItem[] materialItems = requestedItems
            .Where(x => x.Target.Type == EntityType.Material)
            .ToArray();
        if (materialItems.Length > 0)
        {
            Guid[] courseIds = materialItems.Select(x => x.CourseId).Distinct().ToArray();
            Guid[] materialIds = materialItems.Select(x => x.Target.Id).Distinct().ToArray();

            const string materialSql = """
                                        WITH material_candidates AS (
                                            SELECT
                                                ci.course_id AS course_id,
                                                c.slug AS course_slug,
                                                c.title AS course_title,
                                                mat.id AS target_id,
                                                mat.title AS title,
                                                m.title AS section_title,
                                                'Module' AS section_type,
                                                ROW_NUMBER() OVER (
                                                    PARTITION BY ci.course_id, mat.id
                                                    ORDER BY ci.sort_key, mi.sort_key
                                                ) AS rn
                                            FROM course_items ci
                                            JOIN courses c
                                                ON c.id = ci.course_id
                                            JOIN modules m
                                                ON ci.item_type = 'Module'
                                               AND ci.reference_id = m.id
                                            JOIN module_items mi
                                                ON mi.module_id = m.id
                                               AND mi.item_type = 'Material'
                                            JOIN materials mat
                                                ON mat.id = mi.reference_id
                                            WHERE ci.course_id = ANY(@CourseIds)
                                              AND mat.id = ANY(@MaterialIds)
                                              AND c.status = 'PUBLISHED'
                                              AND m.status = 'PUBLISHED'
                                              AND mat.status = 'PUBLISHED'
                                        )
                                        SELECT
                                            course_id AS CourseId,
                                            course_slug AS CourseSlug,
                                            course_title AS CourseTitle,
                                            target_id AS TargetId,
                                            title AS Title,
                                            section_title AS SectionTitle,
                                            section_type AS SectionType
                                        FROM material_candidates
                                        WHERE rn = 1;
                                        """;

            List<ResolvedMaterialRow> materialRows = (await connection.QueryAsync<ResolvedMaterialRow>(
                new CommandDefinition(
                    materialSql,
                    new { CourseIds = courseIds, MaterialIds = materialIds },
                    cancellationToken: cancellationToken))).ToList();

            resolvedItems.AddRange(
                materialRows
                    .Where(x => requestedKeys.Contains((x.CourseId, EntityType.Material, x.TargetId)))
                    .Select(x => new ResolvedMaterialDto(
                        x.CourseId,
                        x.CourseSlug,
                        x.CourseTitle,
                        new EntityReferenceDto(EntityType.Material, x.TargetId),
                        x.Title,
                        x.SectionTitle,
                        x.SectionType)));
        }

        MaterialResolveRequestItem[] issueItems = requestedItems
            .Where(x => x.Target.Type == EntityType.Issue)
            .ToArray();
        if (issueItems.Length > 0)
        {
            Guid[] courseIds = issueItems.Select(x => x.CourseId).Distinct().ToArray();
            Guid[] issueIds = issueItems.Select(x => x.Target.Id).Distinct().ToArray();

            const string issueSql = """
                                     WITH issue_candidates AS (
                                         SELECT
                                             ci.course_id AS course_id,
                                             c.slug AS course_slug,
                                             c.title AS course_title,
                                             i.id AS target_id,
                                             i.title AS title,
                                             m.title AS section_title,
                                             'Module' AS section_type,
                                             1 AS precedence,
                                             ci.sort_key AS course_sort_key,
                                             mi.sort_key AS section_sort_key
                                         FROM course_items ci
                                         JOIN courses c
                                             ON c.id = ci.course_id
                                         JOIN modules m
                                             ON ci.item_type = 'Module'
                                            AND ci.reference_id = m.id
                                         JOIN module_items mi
                                             ON mi.module_id = m.id
                                            AND mi.item_type = 'Issue'
                                         JOIN issues i
                                             ON i.id = mi.reference_id
                                         WHERE ci.course_id = ANY(@CourseIds)
                                           AND i.id = ANY(@IssueIds)
                                           AND c.status = 'PUBLISHED'
                                           AND m.status = 'PUBLISHED'
                                           AND i.status = 'PUBLISHED'

                                         UNION ALL

                                         SELECT
                                             ci.course_id AS course_id,
                                             c.slug AS course_slug,
                                             c.title AS course_title,
                                             i.id AS target_id,
                                             i.title AS title,
                                             p.title AS section_title,
                                             'Project' AS section_type,
                                             2 AS precedence,
                                             ci.sort_key AS course_sort_key,
                                             pi.sort_key AS section_sort_key
                                         FROM course_items ci
                                         JOIN courses c
                                             ON c.id = ci.course_id
                                         JOIN projects p
                                             ON ci.item_type = 'Project'
                                            AND ci.reference_id = p.id
                                         JOIN project_items pi
                                             ON pi.project_id = p.id
                                         JOIN issues i
                                             ON i.id = pi.issue_id
                                         WHERE ci.course_id = ANY(@CourseIds)
                                           AND i.id = ANY(@IssueIds)
                                           AND c.status = 'PUBLISHED'
                                           AND p.status = 'PUBLISHED'
                                           AND i.status = 'PUBLISHED'
                                     ),
                                     ranked AS (
                                         SELECT
                                             course_id,
                                             course_slug,
                                             course_title,
                                             target_id,
                                             title,
                                             section_title,
                                             section_type,
                                             ROW_NUMBER() OVER (
                                                 PARTITION BY course_id, target_id
                                                 ORDER BY precedence, course_sort_key, section_sort_key
                                             ) AS rn
                                         FROM issue_candidates
                                     )
                                     SELECT
                                         course_id AS CourseId,
                                         course_slug AS CourseSlug,
                                         course_title AS CourseTitle,
                                         target_id AS TargetId,
                                         title AS Title,
                                         section_title AS SectionTitle,
                                         section_type AS SectionType
                                     FROM ranked
                                     WHERE rn = 1;
                                     """;

            List<ResolvedIssueRow> issueRows = (await connection.QueryAsync<ResolvedIssueRow>(
                new CommandDefinition(
                    issueSql,
                    new { CourseIds = courseIds, IssueIds = issueIds },
                    cancellationToken: cancellationToken))).ToList();

            resolvedItems.AddRange(
                issueRows
                    .Where(x => requestedKeys.Contains((x.CourseId, EntityType.Issue, x.TargetId)))
                    .Select(x => new ResolvedMaterialDto(
                        x.CourseId,
                        x.CourseSlug,
                        x.CourseTitle,
                        new EntityReferenceDto(EntityType.Issue, x.TargetId),
                        x.Title,
                        x.SectionTitle,
                        x.SectionType)));
        }

        return Result.Success<IReadOnlyCollection<ResolvedMaterialDto>, Error>(resolvedItems);
    }

    private sealed class ResolvedMaterialRow
    {
        public Guid CourseId { get; init; }
        public string CourseSlug { get; init; } = null!;
        public string CourseTitle { get; init; } = null!;
        public Guid TargetId { get; init; }
        public string Title { get; init; } = null!;
        public string SectionTitle { get; init; } = null!;
        public string SectionType { get; init; } = null!;
    }

    private sealed class ResolvedIssueRow
    {
        public Guid CourseId { get; init; }
        public string CourseSlug { get; init; } = null!;
        public string CourseTitle { get; init; } = null!;
        public Guid TargetId { get; init; }
        public string Title { get; init; } = null!;
        public string SectionTitle { get; init; } = null!;
        public string SectionType { get; init; } = null!;
    }
}
