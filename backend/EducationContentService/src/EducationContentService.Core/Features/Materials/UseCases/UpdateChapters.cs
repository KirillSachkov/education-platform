using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Materials;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

/// <summary>
///     Денормализует главы видео (заголовок + offset в секундах) в материалы,
///     привязанные к данному видео. Вызывается MaterialProcessingService после
///     успешного PUT глав в Kinescope. Возвращает количество затронутых материалов;
///     идемпотентно (повторный вызов с тем же набором не публикует event'ов).
/// </summary>
public sealed record UpdateVideoChaptersCommand(
    Guid VideoId,
    Guid AssetVersion,
    IReadOnlyList<VideoChapterDto> Chapters) : ICommand;

public sealed class UpdateVideoChaptersValidator : AbstractValidator<UpdateVideoChaptersCommand>
{
    public UpdateVideoChaptersValidator()
    {
        RuleFor(x => x.VideoId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateVideoChaptersCommand.VideoId)));

        RuleFor(x => x.Chapters)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(UpdateVideoChaptersCommand.Chapters)));
    }
}

public sealed class UpdateVideoChaptersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/internal/videos/{videoId:guid}/chapters/", async Task<EndpointResult<int>> (
                [FromRoute] Guid videoId,
                [FromBody] UpdateVideoChaptersRequest request,
                [FromServices] ICommandHandler<int, UpdateVideoChaptersCommand> handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new UpdateVideoChaptersCommand(
                        videoId,
                        request.AssetVersion,
                        request.Chapters),
                    cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class UpdateVideoChaptersHandler : ICommandHandler<int, UpdateVideoChaptersCommand>
{
    private const int MAX_TITLE_LENGTH = 200;
    private const int MAX_CHAPTERS = 200;

    private readonly IMaterialsRepository _materialsRepository;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UpdateVideoChaptersCommand> _validator;

    public UpdateVideoChaptersHandler(
        IMaterialsRepository materialsRepository,
        IOutboxService outbox,
        ITransactionManager transactionManager,
        IValidator<UpdateVideoChaptersCommand> validator)
    {
        _materialsRepository = materialsRepository;
        _outbox = outbox;
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<int, Error>> Handle(
        UpdateVideoChaptersCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        if (command.Chapters.Count > MAX_CHAPTERS)
        {
            return Error.Validation(
                "material.chapters.too_many",
                $"Слишком много глав (максимум {MAX_CHAPTERS})");
        }

        // Trim titles + drop empties; truncate; preserve caller order. Negative offsets clamp to 0.
        List<string> normalizedTitles = new(command.Chapters.Count);
        List<int> normalizedTimestamps = new(command.Chapters.Count);
        foreach (VideoChapterDto chapter in command.Chapters)
        {
            string title = (chapter.Title ?? string.Empty).Trim();
            if (title.Length == 0)
                continue;
            if (title.Length > MAX_TITLE_LENGTH)
                title = title[..MAX_TITLE_LENGTH];
            normalizedTitles.Add(title);
            normalizedTimestamps.Add(Math.Max(0, chapter.TimeSeconds));
        }

        // EF ValueConverter на Material.VideoId блокирует expression-tree фильтр
        // по полю (`.Value` не транслируется, `m.VideoId == VO` ломается на
        // ChangeType(Guid, VideoId)). Резолвим IDs через Dapper raw SQL —
        // см. GetManyByVideoIdAsync.
        IReadOnlyList<Material> materials = await _materialsRepository
            .GetManyByVideoIdAsync(command.VideoId, cancellationToken);

        if (materials.Count == 0)
            return 0;

        int updated = 0;
        foreach (Material material in materials)
        {
            bool titlesEqual = material.ChapterTitles.SequenceEqual(normalizedTitles, StringComparer.Ordinal);
            bool timestampsEqual = material.ChapterTimestamps.SequenceEqual(normalizedTimestamps);
            if (titlesEqual && timestampsEqual)
                continue;

            material.UpdateChapters(normalizedTitles, normalizedTimestamps);
            await _outbox.PublishAsync(new MaterialUpdated(material.Id));
            updated++;
        }

        if (updated > 0)
        {
            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        return updated;
    }
}
