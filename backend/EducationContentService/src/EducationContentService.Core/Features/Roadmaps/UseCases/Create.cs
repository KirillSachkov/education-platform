using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Roadmaps;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Roadmaps;
using EducationContentService.Domain.ValueObjects;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Roadmaps.UseCases;

public sealed record CreateRoadmapCommand(CreateRoadmapRequest Request) : ICommand;

public class CreateRoadmapCommandValidator : AbstractValidator<CreateRoadmapCommand>
{
    public CreateRoadmapCommandValidator()
    {
        RuleFor(x => x.Request.Title).MustBeValueObject(Title.Create);
        When(x => x.Request.Description is not null, () =>
        {
            RuleFor(x => x.Request.Description!).MustBeValueObject(Description.Create);
        });
        When(x => x.Request.Slug is not null, () =>
        {
            RuleFor(x => x.Request.Slug!)
                .MaximumLength(200)
                .Matches("^[a-z0-9-]+$")
                .WithMessage("Слаг может содержать только строчные латинские буквы, цифры и дефисы");
        });
    }
}

public sealed class CreateRoadmapEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("roadmaps", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateRoadmapRequest request,
                    [FromServices] CreateRoadmapHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new CreateRoadmapCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class CreateRoadmapHandler : ICommandHandler<Guid, CreateRoadmapCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<CreateRoadmapCommand> _validator;
    private readonly ILogger<CreateRoadmapHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public CreateRoadmapHandler(
        IRoadmapsRepository roadmapsRepository,
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        IValidator<CreateRoadmapCommand> validator,
        ILogger<CreateRoadmapHandler> logger,
        UserScopedData userScopedData)
    {
        _roadmapsRepository = roadmapsRepository;
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(CreateRoadmapCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        if (command.Request.CourseId.HasValue)
        {
            Guid courseId = command.Request.CourseId.Value;

            // Course ownership check: only the course owner (or admin) may bind a roadmap to a course.
            Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
                c => c.Id == courseId, cancellationToken);
            if (courseResult.IsFailure)
                return courseResult.Error;

            UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
            if (ownership.IsFailure)
                return ownership.Error;

            bool courseRoadmapExists = await _roadmapsRepository
                .ExistsAsync(r => r.CourseId == courseId, cancellationToken);
            if (courseRoadmapExists)
                return EducationErrors.RoadmapAlreadyExistsForCourse(courseId);
        }

        if (command.Request.Slug is not null)
        {
            string slug = command.Request.Slug;
            bool slugExists = await _roadmapsRepository
                .ExistsAsync(r => r.Slug == slug, cancellationToken);
            if (slugExists)
                return EducationErrors.SlugAlreadyExists(slug);
        }

        Title title = Title.Create(command.Request.Title).Value;
        Description? description = command.Request.Description is not null
            ? Description.Create(command.Request.Description).Value
            : null;

        var roadmap = new Roadmap(
            _userScopedData.UserId,
            title,
            description,
            command.Request.CourseId,
            command.Request.Slug);

        await _roadmapsRepository.AddAsync(roadmap, cancellationToken);

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation("Roadmap created with ID {RoadmapId}", roadmap.Id);

        return roadmap.Id;
    }
}
