using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.Materials;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Materials.UseCases;

/// <summary>
///     Internal batch endpoint — для каждого материала возвращает «primary»
///     привязку к опубликованному курсу (самая ранняя <c>course_materials</c>
///     запись). Используется CommentService inbox handler'ом, чтобы решить —
///     открыть коммент в course-scoped material view (с тредом и closes навигацией)
///     или в standalone KB.
///     Материалы без привязки к published курсу просто не попадают в ответ.
/// </summary>
public sealed record GetMaterialCourseBindingsQuery(IReadOnlyCollection<Guid> Ids) : IQuery;

public sealed class GetMaterialCourseBindingsQueryValidator
    : AbstractValidator<GetMaterialCourseBindingsQuery>
{
    public const int MAX_BATCH_SIZE = 200;

    public GetMaterialCourseBindingsQueryValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull()
            .NotEmpty()
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithMessage($"Количество элементов не должно превышать {MAX_BATCH_SIZE}");
    }
}

public sealed class GetMaterialCourseBindingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("internal/materials/course-bindings",
                async Task<EndpointResult<IReadOnlyList<MaterialCourseBindingLookupDto>>> (
                    [FromBody] GetMaterialCourseBindingsRequest request,
                    [FromServices] GetMaterialCourseBindingsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetMaterialCourseBindingsQuery(request.Ids),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetMaterialCourseBindingsHandler
    : IQueryHandlerWithResult<IReadOnlyList<MaterialCourseBindingLookupDto>, GetMaterialCourseBindingsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetMaterialCourseBindingsQuery> _validator;

    public GetMaterialCourseBindingsHandler(
        ITransactionManager transactionManager,
        IValidator<GetMaterialCourseBindingsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> Handle(
        GetMaterialCourseBindingsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid[] ids = query.Ids.Distinct().ToArray();

        // DISTINCT ON выбирает по первой строке после ORDER BY — берём самую раннюю
        // привязку как primary. У course_materials нет created_at, но id = Guid v7
        // (time-ordered), поэтому order by id даёт стабильный «первый по времени»
        // результат. Отбрасываем DRAFT/ARCHIVED курсы, чтобы не вести юзера на курс,
        // которого он не должен видеть.
        const string sql = """
                           SELECT DISTINCT ON (cm.material_id)
                               cm.material_id AS MaterialId,
                               c.id           AS CourseId,
                               c.slug         AS CourseSlug
                           FROM course_materials cm
                           JOIN courses c ON c.id = cm.course_id AND c.status = 'PUBLISHED'
                           WHERE cm.material_id = ANY(@Ids)
                           ORDER BY cm.material_id, cm.id;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IEnumerable<MaterialCourseBindingLookupDto> rows = await connection.QueryAsync<MaterialCourseBindingLookupDto>(
            new CommandDefinition(sql, new { Ids = ids }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>(rows.ToList());
    }
}
