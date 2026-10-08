using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Roadmaps;
using EducationContentService.Domain.Roadmaps;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Roadmaps.UseCases;

public sealed record SaveCanvasCommand(Guid RoadmapId, SaveCanvasRequest Request) : ICommand;

public class SaveCanvasCommandValidator : AbstractValidator<SaveCanvasCommand>
{
    public SaveCanvasCommandValidator()
    {
        RuleFor(x => x.Request.Nodes).NotNull();
        RuleFor(x => x.Request.Nodes).Must(n => n.Count <= 500).WithMessage("Максимум 500 элементов");
        RuleFor(x => x.Request.Edges).NotNull();
        RuleFor(x => x.Request.Edges).Must(e => e.Count <= 1000).WithMessage("Максимум 1000 связей");
        RuleFor(x => x.Request.Nodes)
            .Must(nodes => nodes.Select(n => n.Id).Distinct().Count() == nodes.Count)
            .WithMessage("Дублирующиеся ID элементов");
        RuleForEach(x => x.Request.Nodes).ChildRules(node =>
        {
            node.RuleFor(n => n.Id).NotEmpty();
            node.RuleFor(n => n.NodeType).NotEmpty();
            node.RuleFor(n => n.Data).NotEmpty();
            node.RuleFor(n => n.Data).MaximumLength(8192);
        });
        RuleForEach(x => x.Request.Edges).ChildRules(edge =>
        {
            edge.RuleFor(e => e.Id).NotEmpty();
            edge.RuleFor(e => e.SourceNodeId).NotEmpty();
            edge.RuleFor(e => e.TargetNodeId).NotEmpty();
            edge.RuleFor(e => e.EdgeType).NotEmpty();
            edge.RuleFor(e => e)
                .Must(e => e.SourceNodeId != e.TargetNodeId)
                .WithMessage("Элемент не может ссылаться сам на себя");
        });
    }
}

public sealed class SaveCanvasEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("roadmaps/{roadmapId:guid}/canvas", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid roadmapId,
                    [FromBody] SaveCanvasRequest request,
                    [FromServices] SaveCanvasHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new SaveCanvasCommand(roadmapId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class SaveCanvasHandler : ICommandHandler<Guid, SaveCanvasCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<SaveCanvasCommand> _validator;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<SaveCanvasHandler> _logger;

    public SaveCanvasHandler(
        IRoadmapsRepository roadmapsRepository,
        ITransactionManager transactionManager,
        IValidator<SaveCanvasCommand> validator,
        UserScopedData userScopedData,
        ILogger<SaveCanvasHandler> logger)
    {
        _roadmapsRepository = roadmapsRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(SaveCanvasCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Roadmap, Error> roadmapResult = await _roadmapsRepository.GetWithChildrenAsync(
            r => r.Id == command.RoadmapId, cancellationToken);
        if (roadmapResult.IsFailure)
            return roadmapResult.Error;

        Roadmap roadmap = roadmapResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(roadmap.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        var nodes = new List<RoadmapNode>();
        for (int i = 0; i < command.Request.Nodes.Count; i++)
        {
            SaveCanvasNodeDto dto = command.Request.Nodes[i];

            if (!Enum.TryParse<RoadmapNodeType>(dto.NodeType, ignoreCase: true, out RoadmapNodeType nodeType))
                return GeneralErrors.ValueIsInvalid($"nodes[{i}].nodeType");

            nodes.Add(new RoadmapNode(
                dto.Id,
                roadmap.Id,
                nodeType,
                dto.PositionX,
                dto.PositionY,
                dto.Width,
                dto.Height,
                dto.ParentNodeId,
                dto.Data,
                dto.SortOrder));
        }

        var edges = command.Request.Edges.Select(dto => new RoadmapEdge(
            dto.Id,
            roadmap.Id,
            dto.SourceNodeId,
            dto.TargetNodeId,
            dto.Label,
            dto.EdgeType,
            dto.Animated,
            dto.SourceHandle,
            dto.TargetHandle)).ToList();

        roadmap.ReplaceCanvas(nodes, edges);

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation(
            "Roadmap {RoadmapId} canvas saved: {NodeCount} nodes, {EdgeCount} edges",
            roadmap.Id, nodes.Count, edges.Count);

        return roadmap.Id;
    }
}
