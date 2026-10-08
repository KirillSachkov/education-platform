using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Roadmaps;
using EducationContentService.Domain;
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

public sealed record UpdateRoadmapCommand(Guid RoadmapId, UpdateRoadmapRequest Request) : ICommand;

public class UpdateRoadmapCommandValidator : AbstractValidator<UpdateRoadmapCommand>
{
    public UpdateRoadmapCommandValidator()
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

public sealed class UpdateRoadmapEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("roadmaps/{roadmapId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid roadmapId,
                    [FromBody] UpdateRoadmapRequest request,
                    [FromServices] UpdateRoadmapHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateRoadmapCommand(roadmapId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class UpdateRoadmapHandler : ICommandHandler<Guid, UpdateRoadmapCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UpdateRoadmapCommand> _validator;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<UpdateRoadmapHandler> _logger;

    public UpdateRoadmapHandler(
        IRoadmapsRepository roadmapsRepository,
        ITransactionManager transactionManager,
        IValidator<UpdateRoadmapCommand> validator,
        UserScopedData userScopedData,
        ILogger<UpdateRoadmapHandler> logger)
    {
        _roadmapsRepository = roadmapsRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(UpdateRoadmapCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Domain.Roadmaps.Roadmap, Error> roadmapResult = await _roadmapsRepository.GetByAsync(
            r => r.Id == command.RoadmapId, cancellationToken);
        if (roadmapResult.IsFailure)
            return roadmapResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(roadmapResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        if (command.Request.Slug is not null)
        {
            string slug = command.Request.Slug;
            Guid roadmapId = command.RoadmapId;
            bool slugExists = await _roadmapsRepository
                .ExistsAsync(r => r.Slug == slug && r.Id != roadmapId, cancellationToken);
            if (slugExists)
                return EducationErrors.SlugAlreadyExists(slug);
        }

        Title title = Title.Create(command.Request.Title).Value;
        Description? description = command.Request.Description is not null
            ? Description.Create(command.Request.Description).Value
            : null;

        roadmapResult.Value.Update(title, description, command.Request.Slug);

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation("Roadmap {RoadmapId} updated", command.RoadmapId);

        return command.RoadmapId;
    }
}
