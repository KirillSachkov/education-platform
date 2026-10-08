using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Core.Features.MockInterviews.UseCases;

public sealed record CreateMockInterviewCommand(
    string? Slug,
    string? Title,
    string? Description,
    IReadOnlyList<Guid>? TopicIds,
    int? SortIndex) : ICommand;

public sealed class CreateMockInterviewCommandValidator : AbstractValidator<CreateMockInterviewCommand>
{
    public CreateMockInterviewCommandValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateMockInterviewCommand.Slug)));

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(CreateMockInterviewCommand.Title)));
    }
}

public sealed class CreateMockInterviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/mock-interviews",
                async Task<EndpointResult<MockInterviewIdResponse>> (
                    CreateMockInterviewRequest request,
                    CreateMockInterviewHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new CreateMockInterviewCommand(
                            request.Slug,
                            request.Title,
                            request.Description,
                            request.TopicIds,
                            request.SortIndex),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Создаёт симуляцию собеседования (admin/seed). Slug уникален. Стартует как DRAFT —
///     публикуется отдельно. SortIndex — явный из запроса либо append (max+1). #568.
/// </summary>
public sealed class CreateMockInterviewHandler : ICommandHandler<MockInterviewIdResponse, CreateMockInterviewCommand>
{
    private readonly IValidator<CreateMockInterviewCommand> _validator;
    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITransactionManager _transactions;

    public CreateMockInterviewHandler(
        IValidator<CreateMockInterviewCommand> validator,
        IMockInterviewsRepository mockInterviews,
        ITransactionManager transactions)
    {
        _validator = validator;
        _mockInterviews = mockInterviews;
        _transactions = transactions;
    }

    public async Task<Result<MockInterviewIdResponse, Error>> Handle(
        CreateMockInterviewCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        string slug = command.Slug!.Trim();
        bool slugTaken = await _mockInterviews.ExistsAsync(m => m.Slug == slug, cancellationToken);
        if (slugTaken)
            return TrainerServiceErrors.MockInterview.SlugAlreadyExists(slug);

        int sortIndex = command.SortIndex ?? await ComputeAppendSortIndexAsync(cancellationToken);

        Result<MockInterview, Error> interviewResult = MockInterview.Create(
            command.Slug,
            command.Title,
            command.Description,
            command.TopicIds ?? [],
            sortIndex);
        if (interviewResult.IsFailure)
            return interviewResult.Error;

        MockInterview interview = interviewResult.Value;
        await _mockInterviews.AddAsync(interview, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new MockInterviewIdResponse(interview.Id);
    }

    private async Task<int> ComputeAppendSortIndexAsync(CancellationToken ct)
    {
        int? maxSortIndex = await _mockInterviews.GetMaxSortIndexAsync(ct);
        return maxSortIndex is null ? 0 : maxSortIndex.Value + 1;
    }
}
