using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.CourseMaterials.UseCases;

public sealed record AttachMaterialToCourseCommand(Guid CourseId, Guid MaterialId) : ICommand;

public sealed class AttachMaterialToCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses/{courseId:guid}/materials",
                async Task<EndpointResult<AttachMaterialToCourseResponse>> (
                    [FromRoute] Guid courseId,
                    [FromBody] AttachMaterialToCourseRequest request,
                    [FromServices] AttachMaterialToCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new AttachMaterialToCourseCommand(courseId, request.MaterialId),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed record AttachMaterialToCourseRequest(Guid MaterialId);

public sealed record AttachMaterialToCourseResponse(Guid MaterialId, bool WasAlreadyAttached);

public sealed class AttachMaterialToCourseHandler : ICommandHandler<AttachMaterialToCourseResponse, AttachMaterialToCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ICourseMaterialsRepository _courseMaterialsRepository;
    private readonly CourseMaterialService _courseMaterialService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<AttachMaterialToCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public AttachMaterialToCourseHandler(
        ICoursesRepository coursesRepository,
        IMaterialsRepository materialsRepository,
        ICourseMaterialsRepository courseMaterialsRepository,
        CourseMaterialService courseMaterialService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<AttachMaterialToCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _materialsRepository = materialsRepository;
        _courseMaterialsRepository = courseMaterialsRepository;
        _courseMaterialService = courseMaterialService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<AttachMaterialToCourseResponse, Error>> Handle(
        AttachMaterialToCourseCommand command,
        CancellationToken cancellationToken)
    {
        var courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        var materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == command.MaterialId, cancellationToken);
        if (materialResult.IsFailure)
            return materialResult.Error;

        // Idempotent: if already attached, return success without creating a duplicate.
        var existingResult = await _courseMaterialsRepository.GetByAsync(
            cm => cm.CourseId == command.CourseId && cm.MaterialId == command.MaterialId,
            cancellationToken: cancellationToken);
        if (existingResult.IsSuccess)
        {
            _logger.LogInformation(
                "Material {MaterialId} already attached to course {CourseId}, idempotent success",
                command.MaterialId, command.CourseId);
            return new AttachMaterialToCourseResponse(command.MaterialId, WasAlreadyAttached: true);
        }

        Result<CourseMaterial, Error> createResult = await _courseMaterialService.CreateAsync(
            command.CourseId, command.MaterialId, cancellationToken);
        if (createResult.IsFailure)
            return createResult.Error;

        // Sync Redis access tags — material is now in an additional course
        // Dapper reads from DB directly, so the new course_materials row (still in EF change tracker)
        // won't be visible yet. Manually ensure the newly attached courseId is included.
        List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(
            command.MaterialId, cancellationToken);

        if (!courseIds.Contains(command.CourseId))
            courseIds.Add(command.CourseId);

        await _outbox.PublishAsync(new MaterialAccessChanged(
            command.MaterialId,
            materialResult.Value.AccessType.ToString(),
            courseIds,
            materialResult.Value.AuthorId));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Material {MaterialId} attached to course {CourseId}",
            command.MaterialId, command.CourseId);

        return new AttachMaterialToCourseResponse(command.MaterialId, WasAlreadyAttached: false);
    }
}
