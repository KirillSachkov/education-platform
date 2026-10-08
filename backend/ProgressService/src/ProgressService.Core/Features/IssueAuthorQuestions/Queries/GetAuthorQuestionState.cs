using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.AuthorQuestions;

namespace ProgressService.Core.Features.IssueAuthorQuestions.Queries;

public sealed record GetAuthorQuestionStateQuery(Guid IssueId) : IQuery;

public sealed class GetAuthorQuestionStateQueryValidator : AbstractValidator<GetAuthorQuestionStateQuery>
{
    public GetAuthorQuestionStateQueryValidator()
    {
        RuleFor(x => x.IssueId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetAuthorQuestionStateQuery.IssueId)));
    }
}

public sealed class GetAuthorQuestionStateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/issues/{issueId:guid}/author-question/",
                async Task<EndpointResult<AuthorQuestionStateResponse>> (
                    [FromRoute] Guid issueId,
                    [FromServices] GetAuthorQuestionStateHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetAuthorQuestionStateQuery(issueId), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     Возвращает состояние «задан ли вопрос автору» для текущего пользователя по заданию (#693).
///     Own-data: Tier-3 entitlement не нужен (контент задания не возвращается). Питает фронт —
///     кнопка показывает «Вопрос отправлен автору» когда <c>AskedAt != null</c>.
/// </summary>
public sealed class GetAuthorQuestionStateHandler
    : IQueryHandlerWithResult<AuthorQuestionStateResponse, GetAuthorQuestionStateQuery>
{
    private readonly IValidator<GetAuthorQuestionStateQuery> _validator;
    private readonly IIssueAuthorQuestionRepository _questions;
    private readonly UserScopedData _user;

    public GetAuthorQuestionStateHandler(
        IValidator<GetAuthorQuestionStateQuery> validator,
        IIssueAuthorQuestionRepository questions,
        UserScopedData user)
    {
        _validator = validator;
        _questions = questions;
        _user = user;
    }

    public async Task<Result<AuthorQuestionStateResponse, Error>> Handle(
        GetAuthorQuestionStateQuery query,
        CancellationToken ct)
    {
        ValidationResult validation = await _validator.ValidateAsync(query, ct);
        if (!validation.IsValid)
            return validation.ToError();

        Guid userId = _user.UserId;
        Guid issueId = query.IssueId;
        IssueAuthorQuestion? question = await _questions.GetByAsync(
            q => q.UserId == userId && q.IssueId == issueId, ct);

        return new AuthorQuestionStateResponse(question?.AskedAt);
    }
}
