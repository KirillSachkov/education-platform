using ContentAccess;
using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Courses.UseCases;

public sealed record CreateCourseCommand(CreateCourseRequest Request) : ICommand;

public class CreateCourseCommandValidator : AbstractValidator<CreateCourseCommand>
{
    public CreateCourseCommandValidator()
    {
        RuleFor(x => x.Request.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Request.Description).MustBeValueObject(Description.Create);
        RuleFor(x => x.Request.Slug).NotEmpty().MustBeValueObject(CourseSlug.Create);

        When(x => !string.IsNullOrWhiteSpace(x.Request.Kind), () =>
            RuleFor(x => x.Request.Kind!)
                .Must(k => Enum.TryParse<CourseKind>(k, ignoreCase: true, out _))
                .WithError(EducationErrors.InvalidCourseKind()));
    }
}

public sealed class CreateCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateCourseRequest request,
                    [FromServices] CreateCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new CreateCourseCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class CreateCourseHandler : ICommandHandler<Guid, CreateCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly OrderingService<Course> _ordering;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IUserGrantWriter _userGrantWriter;
    private readonly IValidator<CreateCourseCommand> _validator;
    private readonly ILogger<CreateCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public CreateCourseHandler(
        ICoursesRepository coursesRepository,
        OrderingService<Course> ordering,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IUserGrantWriter userGrantWriter,
        IValidator<CreateCourseCommand> validator,
        ILogger<CreateCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _ordering = ordering;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _userGrantWriter = userGrantWriter;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(CreateCourseCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Title title = Title.Create(command.Request.Title).Value;
        Description description = Description.Create(command.Request.Description).Value;
        CourseSlug slug = CourseSlug.Create(command.Request.Slug).Value;

        bool slugExists = await _coursesRepository.ExistsAsync(c => c.Slug == slug, cancellationToken);
        if (slugExists)
            return EducationErrors.CourseSlugAlreadyExists(slug.Value);

        SortKey sortKey = await _ordering.ComputeAppendSortKeyAsync(
            c => c.AuthorId == _userScopedData.UserId, cancellationToken);

        CourseKind kind = string.IsNullOrWhiteSpace(command.Request.Kind)
            ? CourseKind.COURSE
            : Enum.Parse<CourseKind>(command.Request.Kind, ignoreCase: true);

        // Catalog-gate (#569): курс админа сразу листится в каталоге, курс обычного
        // автора — нет (ждёт одобрения модератором витрины через PATCH catalog-listing).
        var course = new Course(
            _userScopedData.UserId, title, description, slug, sortKey, kind,
            isCatalogListed: _userScopedData.IsAdmin);

        await _coursesRepository.AddAsync(course, cancellationToken);
        await _outbox.PublishAsync(new CourseCreated(course.Id, _userScopedData.UserId));

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        await _userGrantWriter.GrantAsync(_userScopedData.UserId, GrantTags.Course(course.Id), cancellationToken);

        _logger.LogInformation("Course created with ID {CourseId}", course.Id);

        return course.Id;
    }
}

