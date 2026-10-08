using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Issues;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

/// <summary>
///     Internal batch endpoint — для каждой задачи возвращает «primary» привязку
///     к опубликованному курсу (через project → course_items). Используется
///     CommentService inbox handler'ом, чтобы построить ссылку на course-scoped
///     issue view (<c>/@slug/courses/{courseSlug}/issues/{issueId}</c>).
///     Задачи без привязки к published курсу не попадают в ответ.
/// </summary>
public sealed record GetIssueCourseBindingsQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetIssueCourseBindingsQueryValidator
    : AbstractValidator<GetIssueCourseBindingsQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetIssueCourseBindingsQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetIssueCourseBindingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/issues/course-bindings",
                async Task<EndpointResult<IReadOnlyList<IssueCourseBindingLookupDto>>> (
                    [FromBody] GetIssueCourseBindingsRequest request,
                    [FromServices] GetIssueCourseBindingsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetIssueCourseBindingsQuery(request.Ids),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetIssueCourseBindingsHandler
    : IQueryHandlerWithResult<IReadOnlyList<IssueCourseBindingLookupDto>, GetIssueCourseBindingsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetIssueCourseBindingsQuery> _validator;

    public GetIssueCourseBindingsHandler(
        ITransactionManager transactionManager,
        IValidator<GetIssueCourseBindingsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> Handle(
        GetIssueCourseBindingsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        // issue → project → course_items (item_type='Project') → course.
        // DISTINCT ON (i.id) ORDER BY i.id, ci.id даёт самую раннюю привязку
        // (ci.id = Guid v7, time-ordered) как primary. Только PUBLISHED-курсы —
        // на DRAFT/ARCHIVED не водим юзера.
        const string sql = """
                           SELECT DISTINCT ON (i.id)
                               i.id   AS IssueId,
                               c.id   AS CourseId,
                               c.slug AS CourseSlug
                           FROM issues i
                           JOIN course_items ci
                               ON ci.reference_id = i.project_id
                              AND ci.item_type = 'Project'
                           JOIN courses c
                               ON c.id = ci.course_id
                              AND c.status = 'PUBLISHED'
                           WHERE i.id = ANY(@Ids)
                           ORDER BY i.id, ci.id;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<IssueCourseBindingLookupDto> rows = await connection.QueryAsync<IssueCourseBindingLookupDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<IssueCourseBindingLookupDto>, Error>(rows.ToList());
    }
}
